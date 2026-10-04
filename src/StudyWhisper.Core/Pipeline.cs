namespace StudyWhisper.Core;

/// <summary>Single flight plus two pending WAVs, 30 s expiry. Pause/clear wipe queue and invalidate late results.</summary>
public sealed class Pipeline : IDisposable
{
    private readonly IStudyApi api;
    private readonly Settings settings;
    private readonly SessionCounters? counters;
    private readonly object sync = new();
    private readonly List<Turn> history = new();
    private sealed record AudioWork(byte[] Wav,int Epoch,DateTimeOffset At,TaskCompletionSource<bool> Done,long Id,bool FilterVerified);
    private long nextItem;
    private readonly Queue<AudioWork> queue=new();
    private readonly Timer expiry;
    public const int QueueCapacity=2;
    public static readonly TimeSpan QueueLifetime=TimeSpan.FromSeconds(30);
    private byte[]? currentAudio;
    private bool disposed;
    private CancellationTokenSource lifetime = new();
    private int generation;
    private bool busy, enabled;
    private string pending = "";
    private DateTimeOffset pendingAt;
    private readonly Func<DateTimeOffset> now;
    public event Action<Status>? Changed;
    public event Action<FlowNotice>? Flow;
    public event Action<Turn>? Replied;
    public Pipeline(IStudyApi api, Settings settings, Func<DateTimeOffset>? clock = null,IReadOnlyList<Turn>? initialHistory=null,SessionCounters? counters=null) { this.api=api; this.settings=settings;this.counters=counters; now=clock??(()=>DateTimeOffset.UtcNow); if(initialHistory is not null)history.AddRange(initialHistory.TakeLast(30));expiry=new Timer(_=>{lock(sync)if(!disposed)ExpireQueue();},null,1000,1000); }
    public bool Enabled { get { lock(sync) return enabled; } }
    public int Epoch {get{lock(sync)return generation;}}
    public IReadOnlyList<Turn> History { get { lock(sync) return history.ToArray(); } }
    public int QueueDepth {get{lock(sync){ExpireQueue();return queue.Count;}}}
    public void Enable() { lock(sync){enabled=true;Flow?.Invoke(new(FlowKind.Enabled,generation));Changed?.Invoke(new(Phase.Listening,"Monitoramento ativado"));} }
    public void Pause() => Invalidate(false,false);
    public void Clear() => Invalidate(null,true);
    private void Invalidate(bool? newEnabled,bool clear)
    {
        lock(sync)
        {
            if(disposed)return;
            if(newEnabled.HasValue) enabled=newEnabled.Value;
            generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime=new(); pending="";
            if(currentAudio is not null)Array.Clear(currentAudio);
            while(queue.Count>0){var work=queue.Dequeue();Array.Clear(work.Wav);work.Done.TrySetResult(false);counters?.CancelQueued();}counters?.Queue(0);
            if(clear) history.Clear();
            Flow?.Invoke(new(FlowKind.Cancelled,generation));
            if(enabled)Flow?.Invoke(new(FlowKind.Enabled,generation));
            Changed?.Invoke(new(enabled?Phase.Listening:Phase.Paused,clear?"Memória e histórico limpos":"Monitoramento desativado"));
        }
    }
    private void Report(int epoch,Phase phase,string message) { lock(sync) if(enabled&&epoch==generation) Changed?.Invoke(new(phase,message)); }
    private void Signal(AudioWork work,FlowKind kind){lock(sync)if(enabled&&work.Epoch==generation)Flow?.Invoke(new(kind,generation,work.Id,queue.Count,work.FilterVerified));}
    private void ExpireQueue()
    {
        var expired=false;
        while(queue.Count>0&&now()-queue.Peek().At>=QueueLifetime){var work=queue.Dequeue();Array.Clear(work.Wav);work.Done.TrySetResult(false);counters?.ExpireQueued();expired=true;}
        counters?.Queue(queue.Count);
        if(expired&&enabled)Flow?.Invoke(new(FlowKind.Expired,generation,Queued:queue.Count));
        if(expired&&enabled)Changed?.Invoke(new(Phase.Discarded,"Fala pendente expirou após 30 s. Repita a pergunta."));
    }
    public Task<bool> SubmitAsync(byte[] wav,bool filterVerified=false)
    {
        AudioWork work;
        lock(sync)
        {
            if(disposed||!enabled||wav.Length<44||wav.Length>30*Vad.Rate*2+44){Array.Clear(wav);return Task.FromResult(false);}
            ExpireQueue();work=new(wav,generation,now(),new(TaskCreationOptions.RunContinuationsAsynchronously),++nextItem,filterVerified);
            if(busy)
            {
                if(queue.Count==QueueCapacity){var old=queue.Dequeue();Array.Clear(old.Wav);old.Done.TrySetResult(false);counters?.OverflowQueued();Flow?.Invoke(new(FlowKind.Overflow,generation,work.Id,QueueCapacity));Changed?.Invoke(new(Phase.Discarded,"Fila cheia. Fala mais recente preservada; a pendente mais antiga foi descartada."));}
                queue.Enqueue(work);counters?.Queue(queue.Count,true);Flow?.Invoke(new(FlowKind.Queued,generation,work.Id,queue.Count));return work.Done.Task;
            }
            busy=true;
        }
        _=DrainAsync(work);return work.Done.Task;
    }
    private async Task DrainAsync(AudioWork work)
    {
        while(true)
        {
            var result=await ProcessAsync(work);work.Done.TrySetResult(result);
            lock(sync)
            {
                ExpireQueue();
                if(disposed||!enabled||queue.Count==0){busy=false;return;}
                work=queue.Dequeue();counters?.Queue(queue.Count);Flow?.Invoke(new(FlowKind.Dequeued,generation,work.Id,queue.Count));
            }
        }
    }
    private async Task<bool> ProcessAsync(AudioWork work)
    {
        var wav=work.Wav;var epoch=work.Epoch;CancellationToken token;
        lock(sync){if(disposed||!enabled||epoch!=generation){Array.Clear(wav);return false;}token=lifetime.Token;currentAudio=wav;}
        try
        {
            api.EnsureCanStart();
            Report(epoch,Phase.Transcribing,"Transcrevendo");
            Signal(work,FlowKind.SttStarted);
            var text=(await api.TranscribeAsync(wav,token)).Trim(); token.ThrowIfCancellationRequested();
            Array.Clear(wav);
            if(text.Length==0) { Signal(work,FlowKind.EmptyTranscript);Report(epoch,Phase.Discarded,"Sem fala reconhecida"); return true; }
            Signal(work,FlowKind.TranscriptReady);
            if(text.Length>2000) throw new InvalidDataException("Transcrição excedeu o limite de texto.");
            IReadOnlyList<Turn> memory;
            lock(sync)
            {
                if(epoch!=generation||!enabled) return true;
                if(now()-pendingAt<TimeSpan.FromSeconds(12)) text=(pending+" "+text).Trim();
                pending=""; if(text.Length>4000) text=text[^4000..];
                memory=settings.Memory?history.TakeLast(10).ToArray():Array.Empty<Turn>();
            }
            Report(epoch,Phase.Classifying,"Classificando texto com JEV");
            Signal(work,FlowKind.JevStarted);
            var context=string.Join("\n",memory.Select(x=>$"Pergunta: {x.Question}\nResposta: {x.Answer}"));
            var judgment=await api.ClassifyAsync(text,context,token); token.ThrowIfCancellationRequested();
            if(judgment.Action==Decision.Ignore) { Signal(work,FlowKind.JevIgnored);Report(epoch,Phase.Discarded,"Comentário ou interjeição descartado"); return true; }
            if(judgment.Action==Decision.Wait || judgment.RespondProbability<0.65)
            {
                lock(sync) if(epoch==generation&&enabled) { pending=text; pendingAt=now(); }
                Signal(work,FlowKind.JevWaiting);Report(epoch,Phase.Waiting,"Aguardando continuação da pergunta"); return true;
            }
            Signal(work,FlowKind.JevApproved);Report(epoch,Phase.Thinking,"Preparando resposta");Signal(work,FlowKind.AnswerStarted);
            var result=await api.AnswerAsync(text,memory,token); token.ThrowIfCancellationRequested();
            var answer=Compact(result.Content);
            if(answer.Length==0) throw new InvalidDataException("Modelo retornou resposta vazia.");
            lock(sync)
            {
                if(epoch!=generation||!enabled) return true;
                var turn=new Turn(text,answer,now()){Sources=result.Sources,SearchRequests=result.SearchRequests}; history.Add(turn); if(history.Count>30) history.RemoveAt(0); Replied?.Invoke(turn);
            }
            Signal(work,FlowKind.ResponseReady);Report(epoch,Phase.Listening,"Monitoramento ativado"); return true;
        }
        catch(OperationCanceledException) { return true; }
        catch(ThrottleException ex) { Signal(work,FlowKind.Throttled);Report(epoch,Phase.Throttled,ex.Message); return true; }
        catch(Exception ex) { Signal(work,FlowKind.Error);Report(epoch,Phase.Error,ex is ApiException or InvalidDataException?ex.Message:"Falha ao processar. Verifique rede e configurações."); return true; }
        finally { Array.Clear(wav);lock(sync)currentAudio=null; }
    }
    public static string Compact(string text)
    {
        text=text.Replace("\r\n","\n").Replace('\r','\n').Trim();
        // Preserve requested Markdown and paragraphs while bounding display and memory.
        return text.Length<=6000?text:text[..5999]+"…";
    }
    public void Dispose() { Pause();lock(sync){if(disposed)return;disposed=true;lifetime.Dispose();expiry.Dispose();} }
}
