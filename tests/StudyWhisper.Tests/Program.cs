using System.Net;
using System.Text;
using System.Text.Json;
using StudyWhisper.Core;
using StudyWhisper.App;
using StudyWhisper.TestAudio;

var tests=new List<(string Name,Func<Task> Run)>();
void Add(string name,Action body)=>tests.Add((name,()=>{body();return Task.CompletedTask;}));
void Async(string name,Func<Task> body)=>tests.Add((name,body));
void Assert(bool condition,string message="Assertion failed") {if(!condition)throw new Exception(message);}
void Throws<T>(Action body) where T:Exception {try{body();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
byte[] Audio()=>Wav.Encode(new short[16000]);
JsonElement Json(string json)=>JsonDocument.Parse(json).RootElement.Clone();
AudioBands ToneBands(double hz,double amplitude=.15)
{
    var fft=new Spectrum();AudioBands value=default;
    for(var frame=0;frame<50;frame++){var pcm=new short[320];for(var n=0;n<320;n++)pcm[n]=(short)(32767*amplitude*Math.Sin(2*Math.PI*hz*(frame*320+n)/16000));value=fft.Push(pcm)??value;}
    return value;
}
foreach(var band in new[]{0,1,2})
{
    var selected=band;Add("FFT identifies synthetic frequency band "+band,()=>{var b=ToneBands(selected==0?125:selected==1?1000:4000);var values=new[]{b.Low,b.Mid,b.High};Assert(values[selected]>.5&&values.Where((_,i)=>i!=selected).All(x=>x<.02));Assert(values.All(x=>double.IsFinite(x)&&x>=0&&x<=1));});
}
Add("FFT does not normalize silence into colored activity",()=>{var fft=new Spectrum();for(var n=0;n<100;n++){var v=fft.Push(new short[320]);if(v.HasValue)Assert(v.Value==AudioBands.Silent);}Assert(fft.Analyses==20);Assert(ToneBands(125,.0001)==AudioBands.Silent);});
Add("FFT intensity follows measured amplitude and reset wipes the window",()=>{Assert(ToneBands(1000,.2).Intensity>ToneBands(1000,.01).Intensity);var fft=new Spectrum();for(var n=0;n<5;n++)fft.Push(Enumerable.Repeat((short)10000,320).ToArray());fft.Reset();AudioBands value=default;for(var n=0;n<5;n++)value=fft.Push(new short[320])??value;Assert(value==AudioBands.Silent);});
Add("Silence and incomplete acoustic candidate never emit a rejection",()=>{
    var notices=new List<FlowKind>();using var g=new SpeechGate(new ScriptProbability(_=>0),new());g.Flow+=notices.Add;
    for(var n=0;n<60;n++)g.Push(new short[320]);Assert(notices.Count==0);
    for(var n=0;n<10;n++)g.Push(Enumerable.Repeat((short)6000,320).ToArray());Assert(notices.SequenceEqual(new[]{FlowKind.Candidate}));g.Reset();Assert(!notices.Contains(FlowKind.LocalRejected));
});
Add("Local X follows completed non-speech episode, not its initial frames",()=>{
    var notices=new List<FlowKind>();using var g=new SpeechGate(new ScriptProbability(_=>0),new());g.Flow+=notices.Add;
    for(var n=0;n<50;n++)g.Push(Enumerable.Repeat((short)6000,320).ToArray());Assert(!notices.Contains(FlowKind.LocalRejected));
    for(var n=0;n<60;n++)g.Push(new short[320]);Assert(notices.Count(x=>x==FlowKind.LocalRejected)==1&&!notices.Contains(FlowKind.LocalApproved));
});
Add("Pausing an incomplete acoustic candidate counts cancellation, not a local rejection",()=>{var counters=new SessionCounters();using var gate=new SpeechGate(new ScriptProbability(_=>0),counters);for(var n=0;n<10;n++)gate.Push(Enumerable.Repeat((short)6000,320).ToArray());gate.Reset();Assert(counters.Snapshot().LocalRejected==0&&counters.Snapshot().BufferedDiscarded==1);});
Add("Visual reducer keeps active response while incoming noise or queue overflow arrives",()=>{
    var f=new FlowState();long seq=0;void S(FlowKind k,long item=1,int queue=0)=>f.Apply(new(k,1,++seq,item,queue,true));
    S(FlowKind.Enabled);S(FlowKind.SttStarted);S(FlowKind.TranscriptReady);S(FlowKind.JevStarted);S(FlowKind.JevApproved);S(FlowKind.AnswerStarted);S(FlowKind.LocalRejected);S(FlowKind.Overflow,3,2);
    Assert(f.RemoteBusy&&f.Cloud==StepMark.Active&&f.Jev==StepMark.Approved&&f.Filter==StepMark.Approved&&f.Queued==2&&f.Incoming.Contains("Fila"));S(FlowKind.ResponseReady);Assert(!f.RemoteBusy&&f.Cloud==StepMark.Approved);f.Collapse();Assert(!f.Expanded);
});
Add("Cancel generation rejects queued visual messages and out-of-order delivery",()=>{
    var f=new FlowState();Assert(f.Apply(new(FlowKind.Enabled,1,1)));Assert(f.Apply(new(FlowKind.SttStarted,1,2,1)));
    Assert(f.Apply(new(FlowKind.Cancelled,2,4)));Assert(!f.Apply(new(FlowKind.AnswerStarted,1,3,1))&&!f.Enabled&&!f.Expanded);
    f.Apply(new(FlowKind.Enabled,2,5));f.Apply(new(FlowKind.Candidate,2,7));Assert(!f.Apply(new(FlowKind.LocalRejected,2,6))&&f.Filter==StepMark.Active);
});
Add("JEV wait, discard and error have distinct shapes and no unearned cloud approval",()=>{
    foreach(var result in new[]{FlowKind.JevWaiting,FlowKind.JevIgnored,FlowKind.Error}){var f=new FlowState();long n=0;void S(FlowKind k)=>f.Apply(new(k,1,++n,1,FilterVerified:true));S(FlowKind.Enabled);S(FlowKind.SttStarted);S(FlowKind.TranscriptReady);S(FlowKind.JevStarted);S(result);Assert(f.Jev==(result==FlowKind.JevWaiting?StepMark.Waiting:result==FlowKind.Error?StepMark.Error:StepMark.Rejected)&&f.Cloud==StepMark.Pending&&!f.RemoteBusy);}
});
Async("Live pipeline emits privacy-safe visual stages in actual request order",async()=>{
    var mock=new MockApi();using var p=new Pipeline(mock,new());var events=new List<FlowNotice>();p.Flow+=events.Add;p.Enable();await p.SubmitAsync(Audio(),true);
    Assert(events.Select(e=>e.Kind).SequenceEqual(new[]{FlowKind.Enabled,FlowKind.SttStarted,FlowKind.TranscriptReady,FlowKind.JevStarted,FlowKind.JevApproved,FlowKind.AnswerStarted,FlowKind.ResponseReady}));Assert(events.Skip(1).All(e=>e.Item==1&&e.FilterVerified));
});
Async("Queue expiry and pause emit actual visual notices without false local rejection",async()=>{
    var at=DateTimeOffset.UtcNow;var mock=new MockApi{BlockedStage="stt"};using var p=new Pipeline(mock,new(),()=>at);var events=new List<FlowNotice>();p.Flow+=events.Add;p.Enable();var first=p.SubmitAsync(Audio());await mock.Entered.Task;var second=p.SubmitAsync(Audio());at+=TimeSpan.FromSeconds(31);Assert(p.QueueDepth==0);p.Pause();mock.Release.SetResult();await Task.WhenAll(first,second);
    Assert(events.Any(x=>x.Kind==FlowKind.Queued)&&events.Any(x=>x.Kind==FlowKind.Expired)&&events.Last().Kind==FlowKind.Cancelled&&!events.Any(x=>x.Kind==FlowKind.LocalRejected||x.Kind==FlowKind.ResponseReady));
});
Async("Real neural TTS flows through local gate then mocked requests into the visual reducer",async()=>{
    var model=new FlowState();var kinds=new List<FlowKind>();long seq=0;using var apiPipeline=new Pipeline(new MockApi(),new());
    void Receive(FlowNotice e){kinds.Add(e.Kind);model.Apply(new(e.Kind,e.Epoch,++seq,e.Item,e.Queued,e.FilterVerified));}
    apiPipeline.Flow+=Receive;using var gate=new SpeechGate(new Silero(),new());gate.Flow+=kind=>Receive(new(kind,apiPipeline.Epoch));apiPipeline.Enable();
    foreach(var frame in AudioFixtures.Frames(AudioFixtures.Load(Path.Combine(AppContext.BaseDirectory,"fixtures","pt-BR-short-question.wav")))){var segment=gate.Push(frame);if(segment is not null)await apiPipeline.SubmitAsync(segment.Wav,true);Array.Clear(frame);}
    Assert(kinds.IndexOf(FlowKind.SpeechDetected)<kinds.IndexOf(FlowKind.LocalApproved)&&kinds.IndexOf(FlowKind.LocalApproved)<kinds.IndexOf(FlowKind.SttStarted));Assert(!kinds.Contains(FlowKind.LocalRejected)&&model.Cloud==StepMark.Approved&&model.Jev==StepMark.Approved&&!model.RemoteBusy);
});
Async("HTTP error visual event follows attempted STT without exposing remote body",async()=>{
    var handler=new MockHttp{Error=HttpStatusCode.BadGateway};using var http=new HttpClient(handler);var api=new OpenRouter(http,()=>"TEST-ONLY",new());using var p=new Pipeline(api,new());var f=new FlowState();long seq=0;p.Flow+=e=>f.Apply(new(e.Kind,e.Epoch,++seq,e.Item,e.Queued,e.FilterVerified));p.Enable();await p.SubmitAsync(Audio());Assert(f.Whisper==StepMark.Error&&!f.RemoteBusy&&!f.Reason.Contains("SECRET_REMOTE_BODY"));
});
Add("Missing CRT simulation blocks before factory, with official x64 repair advice",()=>{
    var visited=new List<string>();var status=NativeRuntime.Check(name=>{visited.Add(name);return false;});Assert(!status.Available&&status.UnavailableLibraries.Count==4&&visited.SequenceEqual(NativeRuntime.CrtLibraries));
    var calls=0;try{using var detector=NativeRuntime.Create(status,()=>{calls++;throw new Exception("Should not initialize");});throw new Exception("Expected prerequisite failure");}
    catch(NativeDependencyException ex){Assert(calls==0&&ex.Message.Contains("x64")&&ex.Message.Contains("Microsoft Visual C++")&&NativeRuntime.HelpUri.Host=="learn.microsoft.com");}
});
Add("Partial CRT installation correctly identifies the unavailable library",()=>{var status=NativeRuntime.Check(name=>name!="MSVCP140_1.dll");Assert(!status.Available&&status.UnavailableLibraries.SequenceEqual(new[]{"MSVCP140_1.dll"}));});
foreach(var error in new Exception[]{new DllNotFoundException("SIMULATED-SECRET"),new BadImageFormatException("SIMULATED-SECRET"),new EntryPointNotFoundException("SIMULATED-SECRET"),new TypeInitializationException("fixture",new DllNotFoundException("SIMULATED-SECRET"))})
{
    var selected=error;Add("Native loader fault becomes actionable notice: "+error.GetType().Name,()=>{
        try{using var detector=NativeRuntime.Create(NativeReadiness.Ready,()=>throw selected);throw new Exception("Expected native failure");}
        catch(NativeDependencyException ex){Assert(!ex.Problem.Available&&ex.Message.Contains("reinstale")&&ex.Message.Contains("x64")&&!ex.Message.Contains("SIMULATED-SECRET"));}
    });
}
Add("Non-loader model errors are not misreported as missing Visual C++",()=>{Assert(!NativeRuntime.IsLoaderFailure(new InvalidDataException()));Throws<InvalidDataException>(()=>NativeRuntime.Create(NativeReadiness.Ready,()=>throw new InvalidDataException()));});
Add("Read-only prerequisite probe loads real local filter and resets silence state",()=>{using var detector=NativeRuntime.Create(NativeRuntime.Check());Assert(detector.Predict(new short[512])<.5);});
List<SpeechSegment> Feed(SpeechGate gate,int blocks)
{
    var pcm=new short[blocks*512];for(var n=0;n<pcm.Length;n++)pcm[n]=(short)(n/512+1);
    return AudioFixtures.Frames(pcm,0,0).Select(f=>gate.Push(f)).OfType<SpeechSegment>().ToList();
}

Add("Neural gate preserves pre-roll and full padded ending",()=>{
    using var gate=new SpeechGate(new ScriptProbability(i=>i is >=10 and <=12?.9f:.01f),new());var clips=Feed(gate,50);Assert(clips.Count==1);
    var b=clips[0].Wav;Assert((b.Length-44)/2==19*512);Assert(BitConverter.ToInt16(b,44)==3&&BitConverter.ToInt16(b,b.Length-2)==21);Assert(!clips[0].ForcedBoundary);
});
Add("Two probability blocks are sufficient; no fixed utterance length filter",()=>{
    using var gate=new SpeechGate(new ScriptProbability(i=>i<2?.9f:.01f),new());Assert(Feed(gate,40).Count==1);
});
Add("Hysteresis retains uncertain speech instead of cutting mid phrase",()=>{
    using var gate=new SpeechGate(new ScriptProbability(i=>i<2?.9f:i<32?.4f:.01f),new());var clips=Feed(gate,70);Assert(clips.Count==1&&(clips[0].Wav.Length-44)/2==40*512);
});
Add("Long speech has bounded overlapping chunks, not silent truncation",()=>{
    var counters=new SessionCounters();using var gate=new SpeechGate(new ScriptProbability(_=>.9f),counters,maxSeconds:3);var clips=Feed(gate,250);
    Assert(clips.Count>=2&&clips.All(x=>x.ForcedBoundary&&x.Wav.Length<=3*32000+44));
    Assert(clips[0].Wav.AsSpan(clips[0].Wav.Length-8192).SequenceEqual(clips[1].Wav.AsSpan(44,8192)));Assert(counters.Snapshot().Boundaries==clips.Count&&gate.BufferedSamples<=3*16000+512);
});
Add("Reset wipes residual PCM and recurrent state; pause creates no tail upload",()=>{
    var counters=new SessionCounters();var model=new ScriptProbability(i=>i<5?.9f:0);using var gate=new SpeechGate(model,counters);Feed(gate,4);gate.Reset();Assert(gate.BufferedSamples==0&&model.Resets==1&&counters.Snapshot().BufferedDiscarded==1);
});
Add("Real Silero rejects silence and reduces former 60-second tone uploads",()=>{
    foreach(var voiced in new[]{false,true}){using var gate=new SpeechGate(new Silero(),new());var seconds=0.0;var count=0;for(var f=0;f<3050;f++){var frame=new short[320];if(voiced&&f<3000)for(var i=0;i<320;i++)frame[i]=(short)(6000*Math.Sin(2*Math.PI*220*(f*320+i)/16000));var clip=gate.Push(frame);if(clip is not null){seconds+=(clip.Wav.Length-44)/32000.0;count++;}}Assert(count<=(voiced?1:0)&&seconds<1);Assert(gate.BufferedSamples<=5120+512);}
});
foreach(var name in new[]{"pt-BR-question","pt-BR-short-question","pt-BR-comment"})
{
    var selected=name;Add("Real Silero preserves TTS speech fixture "+name,()=>{
        var pcm=AudioFixtures.Load(Path.Combine(AppContext.BaseDirectory,"fixtures",selected+".wav"));using var gate=new SpeechGate(new Silero(),new());var clips=AudioFixtures.Frames(pcm).Select(f=>gate.Push(f)).OfType<SpeechSegment>().ToArray();
        Assert(clips.Length>0,"Synthetic speech lost before STT");Assert(clips.All(c=>c.Wav.Length>44));
        for(var n=0;n<pcm.Length;n++)if(Math.Abs((int)pcm[n])>16)Assert(clips.Any(c=>n+16000>=c.StartSample&&n+16000<c.EndSample),"Fixture phoneme outside exported margins");
    });
}
Add("Session counters distinguish unknown, partial and reported zero cost",()=>{
    var c=new SessionCounters();c.Monitor(32000);c.Speech(512);c.Call("audio/transcriptions",1.5);c.Call("decisions");c.Cost(0);var s=c.Snapshot();Assert(s.MonitoredSeconds==2&&s.SttSeconds==1.5&&s.CostReports==1&&s.ReportedCost==0&&s.CostText.Contains("restante desconhecido"));
    Assert(new SessionCounters().Snapshot().CostText.Contains("desconhecido"));c.Cost(.0002m);Assert(c.Snapshot().CostText.Contains("cobertura completa"));
});
Add("Isolated neural spike does not hide a locally rejected acoustic episode",()=>{
    var counters=new SessionCounters();using var gate=new SpeechGate(new ScriptProbability(i=>i==0?.6f:0),counters);
    for(var f=0;f<100;f++)Assert(gate.Push(Enumerable.Repeat((short)(f<50?6000:0),320).ToArray()) is null);
    Assert(counters.Snapshot().LocalRejected==1&&counters.Snapshot().SttCalls==0);
});
Async("Portuguese TTS comment reaches textual JEV, not answer, after neural gate",async()=>{
    using var gate=new SpeechGate(new Silero(),new());var mock=new MockApi{Text="Isso é interessante.",Judgment=new(Decision.Ignore,.95,.01)};using var pipeline=new Pipeline(mock,new());pipeline.Enable();
    foreach(var frame in AudioFixtures.Frames(AudioFixtures.Load(Path.Combine(AppContext.BaseDirectory,"fixtures","pt-BR-comment.wav")))){var clip=gate.Push(frame);if(clip is not null)await pipeline.SubmitAsync(clip.Wav);}
    Assert(mock.Calls.Count>0&&!mock.Calls.Contains("answer")&&mock.Calls.Count(x=>x=="stt")==mock.Calls.Count(x=>x=="jev"));
});
Async("Two consecutive questions wait in order without lost second question",async()=>{
    var c=new SessionCounters();var mock=new MockApi{Text="O que é mitose?",BlockedStage="stt"};using var p=new Pipeline(mock,new(),counters:c);p.Enable();var first=p.SubmitAsync(Audio());await mock.Entered.Task;
    mock.Text="E a meiose?";var second=p.SubmitAsync(Audio());Assert(!second.IsCompleted&&p.QueueDepth==1);mock.Release.SetResult();await Task.WhenAll(first,second);
    Assert(p.History.Select(t=>t.Question).SequenceEqual(new[]{"O que é mitose?","E a meiose?"})&&mock.Calls.Count==6&&mock.MemoryCount==1&&c.Snapshot().QueueOverflow==0);
});
Async("Queue overflow preserves latest WAV and wipes oldest pending WAV",async()=>{
    var c=new SessionCounters();var mock=new MockApi{BlockedStage="stt"};using var p=new Pipeline(mock,new(),counters:c);p.Enable();byte[] Mark(int n){var a=Audio();a[44]=(byte)n;return a;}
    var first=p.SubmitAsync(Mark(1));await mock.Entered.Task;var old=Mark(2);var second=p.SubmitAsync(old);var third=p.SubmitAsync(Mark(3));var latest=p.SubmitAsync(Mark(4));
    Assert(!await second&&old.All(x=>x==0)&&p.QueueDepth==2);mock.Release.SetResult();await Task.WhenAll(first,third,latest);
    Assert(mock.AudioMarks.SequenceEqual(new[]{1,3,4})&&c.Snapshot().QueueOverflow==1&&c.Snapshot().LocalRejected==0);
});
foreach(var clearQueue in new[]{false,true})
{
    var clear=clearQueue;Async((clear?"Clear":"Pause/resume")+" wipes queued and active WAV with no old upload after resume",async()=>{
        var c=new SessionCounters();var mock=new MockApi{BlockedStage="stt"};using var p=new Pipeline(mock,new(),counters:c);p.Enable();var active=Audio();active[44]=1;var first=p.SubmitAsync(active);await mock.Entered.Task;
        var queued=Audio();queued[44]=2;var second=p.SubmitAsync(queued);if(clear)p.Clear();else{p.Pause();p.Enable();}
        Assert(!await second&&queued.All(x=>x==0)&&active.All(x=>x==0)&&p.QueueDepth==0);mock.Release.SetResult();await first;
        Assert(mock.AudioMarks.SequenceEqual(new[]{1})&&p.History.Count==0&&c.Snapshot().QueuedCancelled==1);
    });
}
Async("Pending queue expires without sending stale audio",async()=>{
    var at=DateTimeOffset.UtcNow;var c=new SessionCounters();var mock=new MockApi{BlockedStage="stt"};using var p=new Pipeline(mock,new(),()=>at,counters:c);p.Enable();var first=p.SubmitAsync(Audio());await mock.Entered.Task;
    var stale=Audio();var second=p.SubmitAsync(stale);at+=TimeSpan.FromSeconds(31);Assert(p.QueueDepth==0&&!await second&&stale.All(x=>x==0));mock.Release.SetResult();await first;
    Assert(mock.Calls.Count==3&&c.Snapshot().QueueExpired==1);
});
Async("Queued question respects shared HTTP call budget without retry",async()=>{
    var h=new MockHttp{BlockFirst=true};using var http=new HttpClient(h);var budget=new CallBudget(3,3);var c=new SessionCounters();var api=new OpenRouter(http,()=>"TEST-ONLY",new(),budget,counters:c);using var p=new Pipeline(api,new(),counters:c);p.Enable();
    var first=p.SubmitAsync(Audio());await h.Entered.Task;var second=p.SubmitAsync(Audio());h.Release.SetResult();await Task.WhenAll(first,second);Assert(h.Paths.Count==3&&budget.Used==3&&p.History.Count==1&&c.Snapshot().Calls==3);
});
Async("HTTP usage cost and attempted STT duration counted; absent cost unknown",async()=>{
    foreach(var amount in new decimal?[]{null,0,.000123m}){
        var c=new SessionCounters();var h=new MockHttp{ReportedCost=amount};using var http=new HttpClient(h);var api=new OpenRouter(http,()=>"TEST-ONLY",new(),counters:c);await api.TranscribeAsync(Audio(),default);
        var s=c.Snapshot();Assert(s.Calls==1&&s.SttCalls==1&&s.SttSeconds==1&&s.CostReports==(amount.HasValue?1:0)&&s.ReportedCost==(amount??0));
    }
});
Async("JEV question-only contract handles text without punctuation, never audio regex",async()=>{
    var h=new MockHttp();using var http=new HttpClient(h);var api=new OpenRouter(http,()=>"TEST-ONLY",new());await api.ClassifyAsync("Qual a diferença entre mitose e meiose","",default);
    var q=h.Bodies.Single().GetProperty("questions").GetProperty("action");Assert(q.GetProperty("instructions").GetString()!.Contains("APENAS"));Assert(q.GetProperty("criteria").GetProperty("responder").GetString()!.Contains("mesmo sem '?'"));
});

Add("Silence does not trigger STT; pre-roll remains bounded",()=>{var v=new Vad();for(int i=0;i<5000;i++)Assert(v.Push(new short[320]) is null);Assert(v.BufferedSamples<=3200);v.Reset();Assert(v.BufferedSamples==0);});
Add("Synthetic speech emits valid WAV after silence",()=>{var v=new Vad();var clips=Wav.Synthetic().Select(x=>v.Push(x)).Where(x=>x!=null).ToArray();Assert(clips.Length==1);var b=clips[0]!;Assert(Encoding.ASCII.GetString(b,0,4)=="RIFF");Assert(BitConverter.ToInt32(b,24)==16000&&BitConverter.ToInt16(b,22)==1&&BitConverter.ToInt16(b,34)==16);});
Add("Noise burst shorter than minimum speech is discarded",()=>{var v=new Vad();Assert(Wav.Synthetic(5,40).All(x=>v.Push(x)==null));});
Add("Continuous speech is segmented at configured maximum",()=>{var v=new Vad(maxSeconds:3);var clips=Wav.Synthetic(500,0).Select(x=>v.Push(x)).Where(x=>x!=null).ToArray();Assert(clips.Length==3);Assert(clips.All(x=>x!.Length<=3*32000+44));});
Add("VAD reset discards an incomplete segment",()=>{var v=new Vad();foreach(var x in Wav.Synthetic(20,0))v.Push(x);v.Reset();Assert(Wav.Synthetic(0,40).All(x=>v.Push(x)==null));});
Add("Invalid frame and configuration rejected",()=>{Throws<ArgumentException>(()=>new Vad().Push(new short[10]));Throws<ArgumentException>(()=>new Settings{CloseSeconds=1}.Validate());Throws<ArgumentException>(()=>new Settings{VadThreshold=double.NaN}.Validate());});
Add("Public catalog distinguishes STT, JEV and chat",()=>{var models=OpenRouter.BundledModels();Assert(models.Any(x=>x.Id=="openai/whisper-large-v3-turbo"&&x.IsStt));Assert(models.Any(x=>x.Id=="typesafe/jev-1.13"&&x.IsJev));Assert(models.All(x=>x.Id!="typesafe/jev-router"||!x.IsJev));});
Add("JEV typed probabilities accepted",()=>{var d=OpenRouter.ParseJudgment(Json(Fixtures.Jev));Assert(d.Action==Decision.Respond&&d.RespondProbability==.95);});
Add("JEV unknown labels or wrong type fail closed",()=>{Throws<ApiException>(()=>OpenRouter.ParseJudgment(Json(Fixtures.Jev.Replace("responder","evil"))));Throws<ApiException>(()=>OpenRouter.ParseJudgment(Json(Fixtures.Jev.Replace("choice\"","score\""))));Throws<ApiException>(()=>OpenRouter.ParseJudgment(Json("{}")));});
Add("JEV malformed probability distribution fails closed",()=>{Throws<ApiException>(()=>OpenRouter.ParseJudgment(Json(Fixtures.Jev.Replace("0.95","1.95"))));});
Async("Paused pipeline makes zero calls and wipes submitted buffer",async()=>{var mock=new MockApi();using var p=new Pipeline(mock,new());var audio=Audio();Assert(!await p.SubmitAsync(audio));Assert(mock.Calls.Count==0&&audio.All(x=>x==0));});
Async("Question follows STT then JEV then answer and records short paragraph",async()=>{var mock=new MockApi();using var p=new Pipeline(mock,new());p.Enable();await p.SubmitAsync(Audio());Assert(string.Join(",",mock.Calls)=="stt,jev,answer");Assert(p.History.Count==1&&p.History[0].Answer=="Um parágrafo\ncurto.");});
Async("Comments and interjections never call answer",async()=>{foreach(var text in new[]{"Entendi.","Ah!","Isso é interessante."}) {var mock=new MockApi{Text=text,Judgment=new(Decision.Ignore,.9,.01)};using var p=new Pipeline(mock,new());p.Enable();await p.SubmitAsync(Audio());Assert(string.Join(",",mock.Calls)=="stt,jev"&&p.History.Count==0);}});
Async("Empty transcription never calls JEV",async()=>{var mock=new MockApi{Text="  "};using var p=new Pipeline(mock,new());p.Enable();await p.SubmitAsync(Audio());Assert(mock.Calls.SequenceEqual(new[]{"stt"}));});
Async("Low responder probability waits without chat",async()=>{var mock=new MockApi{Judgment=new(Decision.Respond,.5,.6)};using var p=new Pipeline(mock,new());p.Enable();await p.SubmitAsync(Audio());Assert(!mock.Calls.Contains("answer"));});
Async("Wait concatenates next transcript within 12 seconds",async()=>{var mock=new MockApi{Text="Qual a diferença entre",Judgment=new(Decision.Wait,.8,.1)};using var p=new Pipeline(mock,new());p.Enable();await p.SubmitAsync(Audio());mock.Text="mitose e meiose?";mock.Judgment=new(Decision.Respond,.9,.95);await p.SubmitAsync(Audio());Assert(p.History[0].Question=="Qual a diferença entre mitose e meiose?");});
Async("Wait expires and cannot leak into next question",async()=>{var now=DateTimeOffset.UtcNow;var mock=new MockApi{Text="Parte antiga",Judgment=new(Decision.Wait,.8,.1)};using var p=new Pipeline(mock,new(),()=>now);p.Enable();await p.SubmitAsync(Audio());now+=TimeSpan.FromSeconds(13);mock.Text="Pergunta nova?";mock.Judgment=new(Decision.Respond,.9,.95);await p.SubmitAsync(Audio());Assert(p.History[0].Question=="Pergunta nova?");});
foreach(var stage in new[]{"stt","jev","answer"})
{
    var selected=stage;
    Async("Pause cancels and suppresses late results at "+stage,async()=>{var mock=new MockApi{BlockedStage=selected};using var p=new Pipeline(mock,new());p.Enable();var task=p.SubmitAsync(Audio());await mock.Entered.Task;p.Pause();mock.Release.SetResult();await task;Assert(p.History.Count==0&&!p.Enabled);});
}
Async("Single-flight serializes bounded pending audio while busy",async()=>{var mock=new MockApi{BlockedStage="stt"};using var p=new Pipeline(mock,new());p.Enable();var work=p.SubmitAsync(Audio());await mock.Entered.Task;var b=Audio();var next=p.SubmitAsync(b);Assert(!next.IsCompleted&&p.QueueDepth==1);mock.Release.SetResult();await Task.WhenAll(work,next);Assert(mock.Calls.Count==6&&b.All(x=>x==0));});
Async("Clear cancels pending work and deletes history",async()=>{var mock=new MockApi();using var p=new Pipeline(mock,new());p.Enable();await p.SubmitAsync(Audio());mock.BlockedStage="answer";var work=p.SubmitAsync(Audio());await mock.Entered.Task;p.Clear();mock.Release.SetResult();await work;Assert(p.History.Count==0&&p.Enabled);});
Async("Memory default includes previous turns; disabled memory still retains history",async()=>{foreach(var use in new[]{true,false}) {var mock=new MockApi();using var p=new Pipeline(mock,new Settings{Memory=use});p.Enable();await p.SubmitAsync(Audio());await p.SubmitAsync(Audio());Assert(mock.MemoryCount==(use?1:0)&&p.History.Count==2);}});
Async("Session history bounded to 30; context bounded to ten completed pairs",async()=>{var mock=new MockApi();using var p=new Pipeline(mock,new());p.Enable();for(int i=0;i<40;i++)await p.SubmitAsync(Audio());Assert(p.History.Count==30&&mock.MemoryCount==10);});
Add("Markdown structure survives display bounding",()=>{var text=Pipeline.Compact("**Título**\n\n"+new string('x',7000));Assert(text.Length<=6000&&text.Contains('\n')&&text.StartsWith("**Título**"));});
Add("Popover close clock freezes on interaction",()=>{var clock=new PopoverClock();clock.Reset(5);Assert(!clock.Tick(TimeSpan.FromSeconds(4),false));Assert(!clock.Tick(TimeSpan.FromSeconds(100),true));Assert(clock.Tick(TimeSpan.FromSeconds(1),false));});
Add("Call budget enforces minute/session limits including failed calls",()=>{var now=DateTimeOffset.UtcNow;var budget=new CallBudget(3,4,()=>now);for(int i=0;i<3;i++)budget.Take();Throws<ApiException>(budget.Take);now+=TimeSpan.FromSeconds(60);budget.Take();Throws<ApiException>(budget.Take);Assert(budget.Used==4);});
Add("429 cooldown blocks calls without retry and persists configuration",()=>{var now=DateTimeOffset.UtcNow;var budget=new CallBudget(3,4,()=>now);budget.Take();budget.Cooldown(TimeSpan.FromSeconds(120));budget.Configure(10,4);Throws<ApiException>(budget.Take);now+=TimeSpan.FromSeconds(121);budget.Take();Assert(budget.Used==2);});
Async("HTTP mocks verify official endpoints, textual JEV and ZDR on every call",async()=>
{
    var handler=new MockHttp();using var http=new HttpClient(handler);var api=new OpenRouter(http,()=>"TEST-ONLY",new Settings{AnswerModel="mock/text",RequireZdr=true});using var p=new Pipeline(api,new());p.Enable();await p.SubmitAsync(Audio());
    Assert(handler.Paths.SequenceEqual(new[]{"/api/v1/audio/transcriptions","/api/alpha/decisions","/api/v1/chat/completions"}));Assert(handler.Bodies.All(x=>x.GetProperty("provider").GetProperty("zdr").GetBoolean()));
    var stt=handler.Bodies[0];Assert(stt.GetProperty("input_audio").GetProperty("format").GetString()=="wav"&&stt.GetProperty("response_format").GetString()=="json");Assert(Convert.FromBase64String(stt.GetProperty("input_audio").GetProperty("data").GetString()!).Length==32044);
    Assert(handler.Bodies[1].GetProperty("state").GetProperty("transcript").GetString()=="O que é mitose?");Assert(!handler.Bodies[1].ToString().Contains("input_audio"));Assert(!handler.Bodies[2].TryGetProperty("audio",out _));Assert(p.History.Count==1);
});
Async("Validation sends user supplied candidate to GET key, no inference",async()=>{var handler=new MockHttp();using var http=new HttpClient(handler);var api=new OpenRouter(http,()=>throw new Exception("Must not load stored credentials"),new());await api.ValidateKeyAsync("TEST-ONLY",default);Assert(handler.Paths.SequenceEqual(new[]{"/api/v1/key"})&&api.Budget.Used==0);});
Async("Catalog uses requested modality and no Authorization",async()=>{var handler=new MockHttp();using var http=new HttpClient(handler);var api=new OpenRouter(http,()=>throw new Exception("Must not load credentials"),new());await api.ModelsAsync("transcription",default);Assert(handler.Queries.Single()=="?output_modalities=transcription"&&!handler.Auth.Single());});
Async("HTTP errors are redacted and never retried",async()=>{var handler=new MockHttp{Error=HttpStatusCode.Unauthorized};using var http=new HttpClient(handler);var api=new OpenRouter(http,()=>"TEST-ONLY",new());try{await api.TranscribeAsync(Audio(),default);throw new Exception("Expected rejection");}catch(ApiException e){Assert(!e.Message.Contains("SECRET_REMOTE_BODY"));}Assert(handler.Paths.Count==1);});
Async("Timeout while reading response body becomes an error without retry",async()=>{var handler=new MockHttp{StallBody=true};using var http=new HttpClient(handler);var api=new OpenRouter(http,()=>"TEST-ONLY",new(),requestTimeout:TimeSpan.FromMilliseconds(30));try{await api.TranscribeAsync(Audio(),default);throw new Exception("Expected timeout");}catch(ApiException e){Assert(e.Message.Contains("prazo"));}Assert(handler.Paths.Count==1);});
Add("DPAPI round-trip only dummy credential in isolated test directory",()=>{var root=Path.Combine(Path.GetTempPath(),"StudyWhisper-test-"+Guid.NewGuid());try{var store=new Storage(root);Assert(!store.HasKey);store.SaveKey("FAKE-TEST-CREDENTIAL");Assert(store.ReadKey()=="FAKE-TEST-CREDENTIAL");Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"openrouter.dpapi"))).Contains("FAKE-TEST-CREDENTIAL"));store.Save(new());Assert(!File.ReadAllText(Path.Combine(root,"settings.json")).Contains("FAKE-TEST-CREDENTIAL"));store.RemoveKey();Assert(!store.HasKey);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}});

Async("Ten exact completed pairs reach both JEV and answer; ignored/failed turns and clear excluded",async()=>{
    var mock=new MockApi();using var p=new Pipeline(mock,new());p.Enable();
    for(var i=0;i<12;i++){mock.Text=$"Pergunta {i}?";await p.SubmitAsync(Audio());}
    mock.Judgment=new(Decision.Ignore,.9,.02);mock.Text="Comentário irrelevante";await p.SubmitAsync(Audio());
    mock.Judgment=new(Decision.Respond,.9,.95);mock.FailAnswer=true;mock.Text="Pergunta falhou?";await p.SubmitAsync(Audio());
    mock.FailAnswer=false;mock.Text="E nas crianças?";await p.SubmitAsync(Audio());
    Assert(mock.SeenMemory.Select(t=>t.Question).SequenceEqual(Enumerable.Range(2,10).Select(i=>$"Pergunta {i}?")));
    Assert(mock.SeenMemory.All(t=>mock.Context.Contains(t.Question)&&mock.Context.Contains(t.Answer))&&p.History.Count==13);
    Assert(!mock.Context.Contains("irrelevante")&&!mock.Context.Contains("falhou")&&!mock.Context.Contains("crianças"));
    p.Clear();mock.Text="Nova pergunta?";await p.SubmitAsync(Audio());Assert(mock.Context==""&&mock.MemoryCount==0&&p.History.Count==1);
});
Add("Native Markdown copy keeps paragraph/list/code structure without formatting markers",()=>{
    var plain=MarkdownContent.PlainText("**Forte** e *ênfase* com `x`.\n\n- item A\n- [fonte](https://example.org)\n\n```cs\nint x = 1;\n```");
    Assert(plain.Contains("Forte e ênfase com x.")&&plain.Contains("• item A")&&plain.Contains("int x = 1;")&&!plain.Contains("**")&&!plain.Contains("```"));
});
Add("Untrusted links reject script/file/mail/credentials and HTML remains literal",()=>{
    foreach(var link in new[]{"javascript:alert(1)","file:///C:/secret","mailto:a@b","https://user:pass@example.org","not-a-url"})Assert(!SafeLinks.TryParse(link,out _));
    Assert(SafeLinks.TryParse("https://example.org/path",out _));Assert(MarkdownContent.PlainText("<script>alert(1)</script>").Contains("<script>"));
});
Async("Stable response does not report a search; tool remains optional and capped",async()=>{
    var mock=new MockHttp{AnswerJson="""{"choices":[{"message":{"content":"Conceito estável."}}],"usage":{"server_tool_use":{"web_search_requests":0}}}"""};
    using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new());var answer=await api.AnswerAsync("O que é mitose?",Array.Empty<Turn>(),default);
    var body=mock.Bodies.Single();var tool=body.GetProperty("tools")[0];Assert(body.GetProperty("tool_choice").GetString()=="auto"&&body.GetProperty("max_tool_calls").GetInt32()==1);
    Assert(tool.GetProperty("type").GetString()=="openrouter:web_search"&&tool.GetProperty("parameters").GetProperty("max_uses").GetInt32()==1&&tool.GetProperty("parameters").GetProperty("engine").GetString()=="exa");
    Assert(answer.SearchRequests==0&&answer.Sources.Count==0&&mock.Paths.Count==1&&!body.TryGetProperty("plugins",out _));
    var system=body.GetProperty("messages")[0].GetProperty("content").GetString()!;Assert(system.Contains("Não pesquise conceitos estáveis")&&system.Contains("contraindicações")&&system.Contains("100 palavras")&&system.Contains("explicitamente outro formato"));
});
Async("Current response returns discrete citations, deduplicated safe URLs and actual search count",async()=>{
    var mock=new MockHttp{AnswerJson="""{"choices":[{"message":{"content":"Atualização **verificada**.","annotations":[{"type":"url_citation","url_citation":{"title":"Fonte oficial","url":"https://example.org/current"}},{"type":"url_citation","url_citation":{"url":"javascript:alert(1)"}},{"type":"url_citation","url_citation":{"url":"https://example.org/current"}}]}}],"usage":{"server_tool_use":{"web_search_requests":1}}}"""};
    using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new());var answer=await api.AnswerAsync("Qual a diretriz atual?",Array.Empty<Turn>(),default);
    Assert(answer.Sources.Count==1&&answer.Sources[0].Title=="Fonte oficial"&&answer.SearchRequests==1&&answer.Content.Contains("**"));
});
Async("ZDR and opt-out omit search without relaxing provider policy",async()=>{
    foreach(var settings in new[]{new Settings{RequireZdr=true},new Settings{EnableWebSearch=false}}){var mock=new MockHttp();using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",settings);await api.AnswerAsync("Qual o preço atual?",Array.Empty<Turn>(),default);var body=mock.Bodies.Single();Assert(!body.TryGetProperty("tools",out _)&&!body.TryGetProperty("max_tool_calls",out _));Assert(body.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("não foi verificado"));if(settings.RequireZdr)Assert(body.GetProperty("provider").GetProperty("zdr").GetBoolean()&&body.GetProperty("provider").GetProperty("data_collection").GetString()=="deny");}
});
Async("Repeated negatives then question hit local budget, do not retry, and recover after window",async()=>{
    var now=DateTimeOffset.UtcNow;var budget=new CallBudget(12,300,()=>now);var mock=new MockHttp{JevJson=Fixtures.Jev.Replace("\"choice\":\"responder\"","\"choice\":\"ignorar\"")};
    using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new(),budget);using var p=new Pipeline(api,new(),()=>now);var statuses=new List<Status>();p.Changed+=statuses.Add;p.Enable();
    for(var i=0;i<5;i++)await p.SubmitAsync(Audio());Assert(mock.Paths.Count==10&&p.History.Count==0&&budget.Used==10);
    mock.JevJson=Fixtures.Jev;var wav=Audio();await p.SubmitAsync(wav);await p.SubmitAsync(Audio());await p.SubmitAsync(Audio());
    Assert(mock.Paths.Count==10&&wav.All(x=>x==0)&&statuses.Last().Phase==Phase.Throttled&&statuses.Last().Message.Contains("local por minuto")&&statuses.Last().Message.Contains("60 s"));
    now+=TimeSpan.FromSeconds(59);await p.SubmitAsync(Audio());Assert(mock.Paths.Count==10&&statuses.Last().Message.Contains("1 s"));
    now+=TimeSpan.FromSeconds(1);await p.SubmitAsync(Audio());Assert(mock.Paths.Count==13&&p.History.Count==1&&budget.Used==13&&statuses.Last().Phase==Phase.Listening);
});
Async("Remote 429 Retry-After differs from local budget and blocks repeats without network",async()=>{
    var now=DateTimeOffset.UtcNow;var budget=new CallBudget(12,300,()=>now);var mock=new MockHttp{Error=HttpStatusCode.TooManyRequests,RetrySeconds=37};using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new(),budget);
    using var p=new Pipeline(api,new(),()=>now);var statuses=new List<Status>();p.Changed+=statuses.Add;p.Enable();await p.SubmitAsync(Audio());
    Assert(statuses.Last().Phase==Phase.Throttled&&statuses.Last().Message.Contains("HTTP 429")&&statuses.Last().Message.Contains("37 s")&&mock.Paths.Count==1);
    await p.SubmitAsync(Audio());Assert(mock.Paths.Count==1);mock.Error=null;now+=TimeSpan.FromSeconds(37);await p.SubmitAsync(Audio());Assert(mock.Paths.Count==4&&p.History.Count==1);
});
Async("Session budget exhaustion stays throttle and cannot recover by waiting",async()=>{
    var now=DateTimeOffset.UtcNow;var budget=new CallBudget(12,3,()=>now);var mock=new MockHttp();using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new(),budget);using var p=new Pipeline(api,new(),()=>now);var statuses=new List<Status>();p.Changed+=statuses.Add;p.Enable();await p.SubmitAsync(Audio());now+=TimeSpan.FromHours(1);await p.SubmitAsync(Audio());Assert(mock.Paths.Count==3&&statuses.Last().Phase==Phase.Throttled&&statuses.Last().Message.Contains("sessão"));
});

Async("Remote 429 in STT, JEV or answer uses shared cooldown and recovers without duplicate history",async()=>{
    foreach(var path in new[]{"/api/v1/audio/transcriptions","/api/alpha/decisions","/api/v1/chat/completions"})
    {
        var now=DateTimeOffset.UtcNow;var budget=new CallBudget(12,300,()=>now);var mock=new MockHttp{Error=HttpStatusCode.TooManyRequests,ErrorOnlyPath=path,RetrySeconds=17};
        using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new(),budget);using var p=new Pipeline(api,new(),()=>now);var statuses=new List<Status>();p.Changed+=statuses.Add;p.Enable();
        var wav=Audio();await p.SubmitAsync(wav);var calls=mock.Paths.Count;
        Assert(p.History.Count==0&&wav.All(x=>x==0)&&statuses.Last().Phase==Phase.Throttled&&statuses.Last().Message.Contains(path.Split('/').Last()));
        await p.SubmitAsync(Audio());Assert(mock.Paths.Count==calls);mock.Error=null;now+=TimeSpan.FromSeconds(17);await p.SubmitAsync(Audio());Assert(mock.Paths.Count==calls+3&&p.History.Count==1);
    }
});
Async("HTTP response messages contain exactly ten previous pairs plus current question",async()=>{
    var mock=new MockHttp();using var http=new HttpClient(mock);var api=new OpenRouter(http,()=>"TEST-ONLY",new());var turns=Enumerable.Range(0,12).Select(i=>new Turn($"Q{i}",$"A{i}",DateTimeOffset.UtcNow)).ToArray();
    await api.AnswerAsync("E nas crianças?",turns,default);var messages=mock.Bodies.Single().GetProperty("messages");Assert(messages.GetArrayLength()==22);
    for(var i=0;i<10;i++){Assert(messages[1+i*2].GetProperty("content").GetString()==$"Q{i+2}"&&messages[2+i*2].GetProperty("content").GetString()==$"A{i+2}");}
    Assert(messages[21].GetProperty("content").GetString()=="E nas crianças?");
});

int failed=0;foreach(var (name,run) in tests){try{await run();Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed. No personal microphone or authenticated live calls.");return failed==0?0:1;

static class Fixtures {public const string Jev="""{"answers":{"action":{"type":"choice","choice":"responder","confidence":0.9,"probabilities":{"responder":0.95,"aguardar":0.03,"ignorar":0.02}}}}""";}
sealed class MockApi:IStudyApi
{
    public string Text="O que é mitose?",BlockedStage="";public Judgment Judgment=new(Decision.Respond,.9,.95);public List<string> Calls=new();public List<int> AudioMarks=new();public int MemoryCount;public bool FailAnswer;public string Context="";public IReadOnlyList<Turn> SeenMemory=Array.Empty<Turn>();
    public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private async Task Stage(string name){Calls.Add(name);if(BlockedStage==name){Entered.TrySetResult();await Release.Task;}}
    public async Task<string> TranscribeAsync(byte[] wav,CancellationToken token){var text=Text;AudioMarks.Add(wav[44]);await Stage("stt");return text;}
    public async Task<Judgment> ClassifyAsync(string transcript,string context,CancellationToken token){Context=context;await Stage("jev");return Judgment;}
    public async Task<StudyAnswer> AnswerAsync(string transcript,IReadOnlyList<Turn> memory,CancellationToken token){MemoryCount=memory.Count;SeenMemory=memory.ToArray();await Stage("answer");if(FailAnswer)throw new ApiException("Mock failed");return new("Um parágrafo\ncurto.");}
}
sealed class MockHttp:HttpMessageHandler
{
    public List<string> Paths=new(),Queries=new();public List<bool> Auth=new();public List<JsonElement> Bodies=new();public HttpStatusCode? Error;public bool StallBody;public decimal? ReportedCost;public bool BlockFirst;public string? AnswerJson;public string JevJson=Fixtures.Jev;public int RetrySeconds=60;public string? ErrorOnlyPath;
    public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        var path=request.RequestUri!.AbsolutePath;Paths.Add(path);Queries.Add(request.RequestUri.Query);Auth.Add(request.Headers.Authorization!=null);
        if(request.Content!=null)Bodies.Add(JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
        if(BlockFirst&&Paths.Count==1){Entered.TrySetResult();await Release.Task;}
        var json=path switch {"/api/v1/audio/transcriptions"=>"""{"text":"O que é mitose?"}""","/api/alpha/decisions"=>JevJson,"/api/v1/chat/completions"=>"""{"choices":[{"message":{"content":"A mitose divide uma célula em duas."}}]}""","/api/v1/key"=>"""{"data":{"is_free_tier":false}}""",_=>"""{"data":[]}"""};
        if(path=="/api/v1/chat/completions"&&AnswerJson is not null)json=AnswerJson;
        if(ReportedCost.HasValue)json=json[..^1]+",\"usage\":{\"cost\":"+ReportedCost.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}}";
        var effectiveError=ErrorOnlyPath is null||ErrorOnlyPath==path?Error:null;
        var response=new HttpResponseMessage(effectiveError??HttpStatusCode.OK){Content=StallBody?new StreamContent(new StallingStream()):new StringContent(effectiveError.HasValue?"SECRET_REMOTE_BODY":json)};
        if(effectiveError==HttpStatusCode.TooManyRequests)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(RetrySeconds));return response;
    }
}
sealed class StallingStream:MemoryStream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){await Task.Delay(Timeout.Infinite,cancellationToken);return 0;}
}
sealed class ScriptProbability(Func<int,float> probability):ISpeechProbability
{
    private int index;public int Resets;public float Predict(ReadOnlySpan<short> pcm)=>probability(index++);
    public void Reset(){index=0;Resets++;}public void Dispose(){}
}
