using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
namespace StudyWhisper.Core;

public enum RecordingState { Recording, Stopping, Completed, Interrupted }
public sealed record RecordingEntry(string Kind,DateTimeOffset At,long Sequence=0,double From=0,double To=0,string Text="",string Note="",bool Provisional=false,TranscriptWord[]? Words=null,double? Confidence=null,RecordingRevision[]? Revisions=null,RecordingProfile? Profile=null);
public sealed record RecordedSegment(long Sequence,double From,double To,string Text,string Note,bool Pending,bool Provisional=false,bool NeedsReview=false,string OriginalText="",TranscriptWord[]? Words=null,double? Confidence=null,int ContentStart=0,bool Superseded=false);
public sealed record RecordingSnapshot(string Id,DateTimeOffset Started,string Model,RecordingState State,IReadOnlyList<RecordedSegment> Segments,string Notice,RecordingProfile? Profile=null)
{
    private IEnumerable<RecordedSegment> Visible=>Segments.Where(s=>!s.Superseded);
    private static string Render(RecordedSegment s)=>s.Pending?$"[{s.From:0.0}–{s.To:0.0}s: transcrição pendente]":s.Note.Length>0?$"[{s.From:0.0}–{s.To:0.0}s: {s.Note}]":(s.NeedsReview?"[Borda a revisar] ":s.Provisional?"[Provisório] ":"")+s.Text;
    private int StableEnd(RecordedSegment s)=>(s.Provisional||s.NeedsReview)&&Profile is not null&&s.To-s.From>=Profile.WindowSeconds-.001?Math.Min(s.ContentStart+s.Text.Length,RecordingBoundary.StableEnd(s,s.To-Profile.OverlapSeconds)):s.ContentStart;
    public string Transcript=>string.Join(Environment.NewLine,new[]{ConsolidatedTranscript,DraftTranscript}.Where(t=>t.Length>0));
    public string ConsolidatedTranscript
    {
        get{var result=new List<string>();foreach(var s in Visible){if(s.Pending)break;if(s.Provisional||s.NeedsReview){var end=StableEnd(s);if(end>s.ContentStart)result.Add(s.OriginalText[s.ContentStart..end].Trim());break;}result.Add(Render(s));}return string.Join(Environment.NewLine,result.Where(t=>t.Length>0));}
    }
    public string DraftTranscript
    {
        get{var result=new List<string>();var tail=false;foreach(var s in Visible){if(!tail&&!s.Pending&&!s.Provisional&&!s.NeedsReview)continue;if(!tail&&!s.Pending&&(s.Provisional||s.NeedsReview)){tail=true;var end=StableEnd(s);var text=end>s.ContentStart?s.Text[Math.Min(s.Text.Length,end-s.ContentStart)..].Trim():s.Text;if(text.Length>0)result.Add((s.NeedsReview?"[Borda a revisar] ":"[Provisório] ")+text);continue;}tail=true;result.Add(Render(s));}return string.Join(Environment.NewLine,result.Where(t=>t.Length>0));}
    }
    public string OriginalTranscript=>string.Join(Environment.NewLine+Environment.NewLine,Segments.Where(s=>s.OriginalText.Length>0).Select(s=>$"[{s.From:0.0}–{s.To:0.0}s · trecho {s.Sequence}]\n{s.OriginalText}"));
    public bool NeedsReview=>Visible.Any(s=>s.NeedsReview);
    public string Label=>$"{Started.LocalDateTime:d} {Started.LocalDateTime:T} · {StateLabel(State)}";
    public static string StateLabel(RecordingState state)=>state switch{RecordingState.Recording=>"Transcrevendo",RecordingState.Stopping=>"Finalizando",RecordingState.Completed=>"Concluída",_=>"Interrompida"};
}

/// <summary>Append-only text/metadata journal; each accepted result is flushed before publication. No PCM on disk.</summary>
public sealed class RecordingJournal
{
    private readonly string root;
    private readonly object writeSync=new();
    public RecordingJournal(string root){this.root=root;}
    public string Create(string model,RecordingProfile? profile=null)
    {
        Directory.CreateDirectory(root);var id=Guid.NewGuid().ToString("N");
        AppendFile(Path.Combine(root,id+".v2.jsonl"),new("started",DateTimeOffset.UtcNow,Text:model,Profile:profile));return id;
    }
    private string PathFor(string id){if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Sessão inválida.");var v2=Path.Combine(root,id+".v2.jsonl");return File.Exists(v2)?v2:Path.Combine(root,id+".jsonl");}
    private IEnumerable<(string Id,string Path)> Files()
    {
        foreach(var file in Directory.EnumerateFiles(root,"*.jsonl"))
        {
            var name=Path.GetFileNameWithoutExtension(file);
            var id=name.EndsWith(".v2",StringComparison.Ordinal)?name[..^3]:name;
            if(Guid.TryParseExact(id,"N",out _))yield return(id,file);
        }
    }
    public void Append(string id,RecordingEntry entry){lock(writeSync)AppendFile(PathFor(id),entry);}
    private static void AppendFile(string file,RecordingEntry entry)
    {
        var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry)+"\n");
        using var stream=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.Read);
        stream.Write(bytes);stream.Flush(true);
    }
    public IReadOnlyList<RecordingSnapshot> ReadAll()=>!Directory.Exists(root)?Array.Empty<RecordingSnapshot>():Files().Select(f=>f.Id).Distinct(StringComparer.Ordinal).Select(Read).OrderByDescending(s=>s.Started).ToArray();
    public RecordingSnapshot Read(string id)=>ToSnapshot(id,ReadEntries(PathFor(id)));
    private static RecordingSnapshot ToSnapshot(string id,List<RecordingEntry> entries)
    {
        var first=entries.FirstOrDefault(e=>e.Kind=="started");
        var state=RecordingState.Recording;var notice="";var segments=new SortedDictionary<long,RecordedSegment>();
        foreach(var e in entries)
        {
            switch(e.Kind)
            {
                case "queued":segments[e.Sequence]=new(e.Sequence,e.From,e.To,"","",true);break;
                case "text":case "gap":segments[e.Sequence]=new(e.Sequence,e.From,e.To,e.Text,e.Note,false,e.Provisional,false,e.Text,e.Words,e.Confidence);break;
                case "revise":if(e.Revisions is not null)foreach(var r in e.Revisions)if(segments.TryGetValue(r.Sequence,out var old))segments[r.Sequence]=old with{Text=r.Text,Provisional=r.Provisional,NeedsReview=r.NeedsReview,ContentStart=r.ContentStart,Superseded=r.Superseded};break;
                case "stopping":state=RecordingState.Stopping;break;
                case "completed":state=RecordingState.Completed;notice=e.Note;break;
                case "interrupted":state=RecordingState.Interrupted;notice=e.Note;break;
            }
        }
        return new(id,first?.At??DateTimeOffset.MinValue,first?.Text??"",state,segments.Values.ToArray(),notice,first?.Profile);
    }
    private static List<RecordingEntry> ReadEntries(string file,bool recover=false)
    {
        byte[] bytes;
        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        {
            bytes=new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }
        var result=new List<RecordingEntry>();
        var offset=0;
        var copied=0;
        var temporary=file+".migration.tmp";
        FileStream? output=null;
        try
        {
            // A previous process can only have left this scratch file before the atomic replacement.
            if(recover&&File.Exists(temporary))File.Delete(temporary);
            while(offset<bytes.Length)
            {
                var newline=Array.IndexOf(bytes,(byte)'\n',offset);
                var end=newline>=0?newline:bytes.Length;
                var line=bytes.AsMemory(offset,end-offset);
                RecordingEntry? entry;
                try{entry=JsonSerializer.Deserialize<RecordingEntry>(line.Span);}
                catch(JsonException){entry=null;}
                if(entry is null||entry.Text is null||entry.Note is null||entry.Sequence<0||
                    !double.IsFinite(entry.From)||!double.IsFinite(entry.To)||entry.From<0||entry.To<entry.From||
                    entry.Kind is not ("started" or "queued" or "text" or "gap" or "revise" or "stopping" or "completed" or "interrupted")||
                    entry.Revisions?.Any(r=>r is null||r.Text is null||r.Sequence<0||r.ContentStart<0)==true)
                {
                    if(recover&&newline>=0&&newline+1<bytes.Length)
                        throw new IOException("Registro inválido antes do fim do arquivo. Histórico preservado.");
                    break;
                }
                result.Add(entry);
                if(recover&&HasLegacyProperty(line.Span))
                {
                    using var json=JsonDocument.Parse(line);
                    if(json.RootElement.EnumerateObject().Any(p=>IsLegacyProperty(p.Name)))
                    {
                        output??=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                        output.Write(bytes.AsSpan(copied,offset-copied));
                        using(var writer=new Utf8JsonWriter(output))
                        {
                            writer.WriteStartObject();
                            foreach(var property in json.RootElement.EnumerateObject())
                                if(!IsLegacyProperty(property.Name))property.WriteTo(writer);
                            writer.WriteEndObject();
                        }
                        output.WriteByte((byte)'\n');
                        copied=newline>=0?newline+1:end;
                    }
                }
                offset=newline>=0?newline+1:end;
            }
            if(recover)
            {
                if(output is not null)
                {
                    output.Write(bytes.AsSpan(copied,offset-copied));
                    if(offset>copied&&bytes[offset-1]!='\n')output.WriteByte((byte)'\n');
                    output.Flush(true);output.Dispose();
                    File.Move(temporary,file,true);
                }
                else if(offset<bytes.Length||offset>0&&bytes[offset-1]!='\n')
                {
                    using var stream=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.Read);
                    stream.SetLength(offset);stream.Position=offset;
                    if(offset>0&&bytes[offset-1]!='\n')stream.WriteByte((byte)'\n');
                    stream.Flush(true);
                }
            }
            return result;
        }
        finally
        {
            if(output is not null){output.Dispose();File.Delete(temporary);}
        }
    }

    private static bool HasLegacyProperty(ReadOnlySpan<byte> line)=>
        line.IndexOf("\"FinalText\""u8)>=0||line.IndexOf("\"RewriteState\""u8)>=0||line.IndexOf("\"RewriteNotice\""u8)>=0;
    private static bool IsLegacyProperty(string key)=>key is "FinalText" or "RewriteState" or "RewriteNotice";

    public void Recover()
    {
        if(!Directory.Exists(root))return;
        if(File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Pasta de histórico redirecionada.");
        var incomplete=false;
        lock(writeSync)
        foreach(var (id,file) in Files())
        {
            try{RecoverFile(id,file);}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){incomplete=true;}
        }
        if(incomplete)throw new IOException("Não foi possível atualizar todo o histórico local.");
    }
    private static void RecoverFile(string id,string file)
    {
        if(File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Arquivo de histórico redirecionado.");
        var snapshot=ToSnapshot(id,ReadEntries(file,recover:true));
        if(snapshot.State is RecordingState.Recording or RecordingState.Stopping||snapshot.Segments.Any(s=>s.Pending))
        {
            foreach(var s in snapshot.Segments.Where(s=>s.Pending))AppendFile(file,new("gap",DateTimeOffset.UtcNow,s.Sequence,s.From,s.To,Note:"áudio temporário perdido ao encerrar; sem repetição automática"));
            var drafts=snapshot.Segments.Where(s=>s.Provisional&&!s.Superseded).Select(s=>new RecordingRevision(s.Sequence,s.Text,false,true,s.ContentStart)).ToArray();if(drafts.Length>0)AppendFile(file,new("revise",DateTimeOffset.UtcNow,Revisions:drafts));
            AppendFile(file,new("interrupted",DateTimeOffset.UtcNow,Note:"Sessão encerrada antes da conclusão. Texto já transcrito preservado; o trecho final ainda não transcrito pode estar ausente."));
        }
    }
}

/// <summary>Continuous overlapping PCM windows and a revisable first preview. Serial STT only; no VAD/JEV or queue eviction.</summary>
public sealed class RecordingSession : IDisposable
{
    private sealed record Work(long Sequence,double From,double To,byte[] Wav,long Section);
    private readonly object sync=new();
    private readonly RecordingJournal journal;
    private readonly IStudyApi api;
    private readonly Queue<Work> queue=new();
    private readonly List<short> buffer=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly List<RecordedSegment> segments=new();
    private readonly DateTimeOffset started=DateTimeOffset.UtcNow;
    private readonly string model;
    private readonly RecordingProfile profile;
    private long samples,sequence,section,sectionStart,lastQueuedEnd,lastAccepted,acceptedSection,queuedSamples;
    private bool firstFullWindow;
    private bool accepting=true,busy,disposed,hasGaps;
    private bool storageFailed,remoteFailed;
    private string notice="";
    private Task? stopTask;
    private RecordingState state=RecordingState.Recording;
    private TaskCompletionSource<bool> drained=Completed();
    private static TaskCompletionSource<bool> Completed(){var t=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);t.SetResult(true);return t;}
    public string Id {get;}
    public event Action? Changed;
    public event Action? StopRequested;
    public RecordingSnapshot Snapshot {get{lock(sync)return new(Id,started,model,state,segments.ToArray(),notice,profile);}}
    public bool Accepting {get{lock(sync)return accepting&&!disposed;}}
    public Task WhenIdle {get{lock(sync)return stopTask is null?drained.Task:Task.WhenAll(drained.Task,stopTask);}}
    public RecordingSession(RecordingJournal journal,IStudyApi api,string model,RecordingProfile? profile=null){this.journal=journal;this.api=api;this.model=model;this.profile=profile??new();this.profile.Validate();Id=journal.Create(model,this.profile);}
    public void Push(ReadOnlySpan<short> pcm)
    {
        lock(sync)
        {
            if(!accepting||disposed)return;
            foreach(var sample in pcm)
            {
                if(!accepting)break;buffer.Add(sample);samples++;
                if(!firstFullWindow&&profile.PreviewSeconds>0&&samples-sectionStart==profile.PreviewSeconds*Vad.Rate)Enqueue();
                if(buffer.Count==profile.WindowSeconds*Vad.Rate){Enqueue();RetainOverlap();firstFullWindow=true;}
            }
        }
    }
    private void Enqueue()
    {
        if(buffer.Count==0||samples==lastQueuedEnd)return;
        var from=(double)(samples-buffer.Count)/Vad.Rate;var to=(double)samples/Vad.Rate;var id=++sequence;
        // Backpressure stops capture explicitly instead of evicting earlier segments.
        if(queuedSamples+buffer.Count>120*Vad.Rate)
        {
            Gap(id,from,to,"fila de transcrição cheia; gravação interrompida");ClearBuffer();accepting=false;state=RecordingState.Stopping;Changed?.Invoke();StopRequested?.Invoke();return;
        }
        if(!Persist(new("queued",DateTimeOffset.UtcNow,id,from,to))){segments.Add(new(id,from,to,"","armazenamento indisponível; bloco não enviado",false));ClearBuffer();Changed?.Invoke();StopRequested?.Invoke();return;}
        segments.Add(new(id,from,to,"","",true));var wav=Wav.Encode(CollectionsMarshal.AsSpan(buffer));
        queue.Enqueue(new(id,from,to,wav,section));queuedSamples+=buffer.Count;lastQueuedEnd=samples;
        if(!busy){busy=true;drained=new(TaskCreationOptions.RunContinuationsAsynchronously);_=Task.Run(DrainAsync);}
        Changed?.Invoke();
    }
    private void ClearBuffer(){CollectionsMarshal.AsSpan(buffer).Clear();buffer.Clear();}
    private void RetainOverlap()
    {
        var count=Math.Min(buffer.Count,profile.OverlapSeconds*Vad.Rate);var tail=count==0?Array.Empty<short>():CollectionsMarshal.AsSpan(buffer)[^count..].ToArray();
        ClearBuffer();buffer.AddRange(tail);Array.Clear(tail);
    }
    private void Revise(params RecordingRevision[] revisions)
    {
        foreach(var r in revisions){var index=segments.FindIndex(s=>s.Sequence==r.Sequence);if(index>=0)segments[index]=segments[index] with{Text=r.Text,Provisional=r.Provisional,NeedsReview=r.NeedsReview,ContentStart=r.ContentStart,Superseded=r.Superseded};}
        if(revisions.Length>0&&!Persist(new("revise",DateTimeOffset.UtcNow,Revisions:revisions)))StopRequested?.Invoke();
    }
    private void SealLast(bool review=false)
    {
        var previous=segments.FirstOrDefault(s=>s.Sequence==lastAccepted);
        if(previous is {Provisional:true})Revise(new RecordingRevision(previous.Sequence,previous.Text,false,previous.NeedsReview||review,previous.ContentStart));
    }
    private void Accept(Work work,RecordedTranscript result)
    {
        var text=result.Text.Trim();var index=segments.FindIndex(s=>s.Sequence==work.Sequence);
        var current=new RecordedSegment(work.Sequence,work.From,work.To,text,"",false,true,false,text,result.Words,result.Confidence);segments[index]=current;
        if(!Persist(new("text",DateTimeOffset.UtcNow,work.Sequence,work.From,work.To,text,Provisional:true,Words:result.Words,Confidence:result.Confidence)))StopRequested?.Invoke();
        var previous=segments.FirstOrDefault(s=>s.Sequence==lastAccepted);
        if(previous is not null)
        {
            if(acceptedSection!=work.Section)SealLast();
            else
            {
                var changes=RecordingBoundary.Reconcile(previous,current);
                if(changes is not null)Revise(changes);
                else
                {
                    Revise(new(previous.Sequence,previous.Text,false,true,previous.ContentStart),new(current.Sequence,current.Text,true,true));
                    if(!storageFailed)notice="Alguns trechos podem repetir palavras porque não foi possível alinhar o áudio com segurança.";
                }
            }
        }
        lastAccepted=work.Sequence;acceptedSection=work.Section;Changed?.Invoke();
    }
    private void Gap(long id,double from,double to,string reason)
    {
        Persist(new("gap",DateTimeOffset.UtcNow,id,from,to,Note:reason));
        var segment=new RecordedSegment(id,from,to,"",reason,false);var index=segments.FindIndex(s=>s.Sequence==id);if(index<0)segments.Add(segment);else segments[index]=segment;hasGaps=true;
    }
    private async Task DrainAsync()
    {
        while(true)
        {
            Work work;
            lock(sync){if(disposed||queue.Count==0){busy=false;drained.TrySetResult(true);return;}work=queue.Dequeue();queuedSamples-=(work.Wav.Length-44)/2;}
            try
            {
                api.EnsureCanStart();var result=api is IRecordingApi timed?await timed.TranscribeRecordingAsync(work.Wav,lifetime.Token).ConfigureAwait(false):new RecordedTranscript(await api.TranscribeAsync(work.Wav,lifetime.Token).ConfigureAwait(false));
                lock(sync)
                {
                    if(disposed)continue;
                    Accept(work,result);
                }
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                lock(sync)
                {
                    if(!disposed)
                    {
                        Gap(work.Sequence,work.From,work.To,"falha na transcrição; trecho não repetido automaticamente");
                        // Stop on a failed paid stage or cooldown; drain pre-existing work without retrying this segment.
                        accepting=false;hasGaps=true;remoteFailed=true;state=RecordingState.Stopping;if(!storageFailed)notice="Falha na transcrição. Confira conexão, saldo e configurações. Texto salvo preservado.";
                        Changed?.Invoke();StopRequested?.Invoke();
                    }
                }
            }
            finally{Array.Clear(work.Wav);}
        }
    }
    private void StorageFailure(){accepting=false;hasGaps=true;storageFailed=true;state=RecordingState.Stopping;notice="Não foi possível salvar. O texto desta sessão continua disponível para copiar enquanto o app estiver aberto.";}
    private bool Persist(RecordingEntry entry)
    {
        if(storageFailed)return false;
        try{journal.Append(Id,entry);return true;}catch(IOException){StorageFailure();return false;}catch(UnauthorizedAccessException){StorageFailure();return false;}
    }
    public void CaptureGap()
    {
        lock(sync)
        {
            if(!accepting||disposed)return;
            Enqueue();ClearBuffer();section++;sectionStart=samples;firstFullWindow=false;Gap(++sequence,(double)samples/Vad.Rate,(double)samples/Vad.Rate,"entrada de áudio interrompida; duração da lacuna desconhecida");Changed?.Invoke();
        }
    }
    public Task StopAsync()
    {
        lock(sync)
        {
            if(stopTask is not null)return stopTask;
            if(disposed||state is RecordingState.Completed or RecordingState.Interrupted)return Task.CompletedTask;
            accepting=false;state=RecordingState.Stopping;
            return stopTask=Task.Run(StopCoreAsync);
        }
    }
    private async Task StopCoreAsync()
    {
        Task wait;
        lock(sync)
        {
            if(disposed||state is RecordingState.Completed or RecordingState.Interrupted)return;
            accepting=false;state=RecordingState.Stopping;
            if(remoteFailed){if(samples>lastQueuedEnd)Gap(++sequence,(double)lastQueuedEnd/Vad.Rate,(double)samples/Vad.Rate,"trecho final não enviado após falha do STT; sem repetição automática");}else Enqueue();ClearBuffer();Persist(new("stopping",DateTimeOffset.UtcNow));
            wait=drained.Task;Changed?.Invoke();
        }
        await wait.ConfigureAwait(false);
        lock(sync)
        {
            if(disposed)return;SealLast();state=hasGaps?RecordingState.Interrupted:RecordingState.Completed;
            if(!Persist(new(hasGaps?"interrupted":"completed",DateTimeOffset.UtcNow,Note:notice)))state=RecordingState.Interrupted;
            Changed?.Invoke();
        }
    }
    public void Dispose()
    {
        lock(sync)
        {
            if(disposed)return;disposed=true;accepting=false;lifetime.Cancel();ClearBuffer();
            while(queue.TryDequeue(out var work))Array.Clear(work.Wav);
            if(state is RecordingState.Recording or RecordingState.Stopping)
            {
                state=RecordingState.Interrupted;
                notice="App encerrado antes de concluir. Texto transcrito preservado; áudio temporário pendente descartado.";
                for(var index=0;index<segments.Count;index++)if(segments[index].Pending)
                    segments[index]=segments[index] with{Pending=false,Note="áudio temporário descartado ao encerrar; trecho não transcrito"};
                try{journal.Append(Id,new("interrupted",DateTimeOffset.UtcNow,Note:notice));}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
}
