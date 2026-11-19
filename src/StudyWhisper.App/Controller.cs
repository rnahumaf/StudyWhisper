using System.Net.Http;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace StudyWhisper.App;

public enum StudyMode { Questions, Transcription }

public sealed class Controller : IDisposable
{
    public Storage Store {get;}
    public Settings Settings {get;private set;}
    public bool Demo {get;}
    public SessionCounters Counters {get;}=new();
    public string HistoryNotice {get;}="";
    private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
    private readonly CallTracker tracker;
    private readonly Forms.NotifyIcon? tray;
    public OrbWindow Orb {get;}
    public PopoverWindow Popover {get;}
    private readonly CaptureControls controls;
    public StudyMode SelectedMode {get;private set;}=StudyMode.Questions;
    private DashboardWindow? dashboard;
    private readonly List<SessionTranscript> sessionTranscripts=new();
    private Pipeline pipeline=null!;
    private readonly CaptureRecovery capture;
    private readonly UpdateSession? updateSession;
    private readonly DispatcherTimer healthTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private long processedFrames,processingFaults;
    private int consecutiveFaults;
    private volatile bool processingBlocked;
    private CaptureStatus? captureStatus;
    private Status lastStatus=new(Phase.Paused,"Microfone desligado");
    private OutsideClickDismissal? trayDismissal;
    private SpeechGate? gate;
    private Vad? demoVad;
    private readonly object audioLock=new();
    private readonly Spectrum spectrum=new();
    private long visualSession,visualSequence;
    private readonly object visualDispatch=new();
    private int spectrumPosted;
    private sealed record SpectrumFrame(Pipeline Source,int Epoch,AudioBands Bands);
    private SpectrumFrame? latestSpectrum;
    private DependencyWindow? dependencyWindow;
    public NativeReadiness DependencyStatus {get;private set;}=NativeReadiness.Ready;
    private readonly DispatcherTimer throttleTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private string activeKey="";
    private readonly DemoApi demoApi=new();
    private bool disposed;
    private readonly RecordingJournal recordings;
    private RecordingSession? recording;
    private bool savedRecordingsLoaded;
    private readonly Dictionary<string,RecordingSnapshot> completedRecordings=new();
    private long modeVersion;
    private Task interruptedRecordingIdle=Task.CompletedTask;
    private bool changingMode,stoppingRecording;
    public string? ActiveRecordingId=>recording?.Id;
    public bool RecordingActive=>recording is not null;
    public bool ModeReady=>!changingMode&&!stoppingRecording;
    private readonly Dispatcher dispatcher=System.Windows.Application.Current.Dispatcher;
    public Controller(bool demo,bool smoke=false,string? dataRoot=null)
    {
        Demo=demo;Store=new Storage(dataRoot??(demo?Path.Combine(Path.GetTempPath(),"StudyWhisper-demo-unused"):Storage.DefaultRoot));Settings=demo?new Settings{AnswerModel="demo/offline"}:Store.Load();tracker=new();
        recordings=new RecordingJournal(Path.Combine(dataRoot??(smoke?Path.Combine(Path.GetTempPath(),"StudyWhisper-recording-smoke-"+Guid.NewGuid()):demo?Path.Combine(Path.GetTempPath(),"StudyWhisper-recording-demo"):Storage.DefaultRoot),"recordings"));
        try{recordings.Recover();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){HistoryNotice="Não foi possível atualizar todo o histórico. Confira as permissões da pasta de transcrições e reabra o app.";}
        try{Store.DiscardLegacyRewriteWork();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){HistoryNotice=(HistoryNotice+" Não foi possível concluir a limpeza dos rascunhos antigos. Confira o acesso ao armazenamento e reabra o app.").Trim();}
        updateSession=demo||smoke?null:Store.ReadUpdateSession();if(updateSession is not null)tracker.RestoreUsedForUpdate(updateSession.UsedCalls);if(updateSession?.CounterState is not null)Counters.Restore(updateSession.CounterState);
        Orb=new OrbWindow(ShowPanel,Clear,()=>System.Windows.Application.Current.Shutdown());Popover=new PopoverWindow(Clear);
        Orb.Configure(Settings.ReduceMotion);
        CreatePipeline(updateSession?.History);
        controls=new CaptureControls(this,compact:true);Orb.AttachTools(controls);
        capture=new(()=>new Microphone(),OnFrame,ResetCaptureState);
        capture.Changed+=state=>
        {
            var captureEpoch=capture.Generation;
            if(state.Fault is not null&&!Demo)Store.RecordCaptureFault(state.State.ToString(),state.Attempt,state.Fault.Operation,state.Fault.Code,state.Fault.Category);
            Dispatch(()=>
            {
                if(disposed||(!pipeline.Enabled&&recording is null)||captureEpoch!=capture.Generation)return;captureStatus=state;
                if(recording is not null)
                {
                    if(state.State==CaptureState.Recovering)try{recording.CaptureGap();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){_=StopRecordingAsync();}
                    if(state.State==CaptureState.Blocked){recording.CaptureGap();_=StopRecordingAsync();}
                    RefreshRecording();return;
                }
                if(state.State==CaptureState.Ready)Orb.EndRecovery();
                RefreshCaptureHealth();ReceiveStatus(lastStatus);UpdateTray();
                if(state.State==CaptureState.Ready&&updateSession is not null)Store.CompleteUpdateSession();
            });
        };
        healthTimer.Tick+=(_,_)=>{if(disposed){healthTimer.Stop();return;}RefreshCaptureHealth();};
        throttleTimer.Tick+=(_,_)=>
        {
            if(disposed||!pipeline.Enabled){throttleTimer.Stop();return;}
            try{tracker.EnsureAvailable();throttleTimer.Stop();Orb.EndThrottle();}
            catch(ThrottleException ex){ReceiveStatus(new(Phase.Throttled,ex.Message));if(ex.RetryAfter is null)throttleTimer.Stop();}
        };
        if(!smoke)
        {
            tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="StudyWhisper • Microfone desligado",Visible=true};
            var menu=new Forms.ContextMenuStrip{Font=new System.Drawing.Font("Segoe UI",8.25f)};
            menu.Items.Add("Abrir painel",null,(_,_)=>ShowPanel());menu.Items.Add("Limpar memória",null,(_,_)=>Clear());menu.Items.Add("Sair",null,(_,_)=>System.Windows.Application.Current.Shutdown());
            menu.AutoClose=true;
            menu.Opened+=(_,_)=>{trayDismissal?.Dispose();trayDismissal=new(()=>new[]{menu.Handle},()=>menu.Close());};
            menu.Closed+=(_,_)=>{trayDismissal?.Dispose();trayDismissal=null;};
            tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowPanel();
        }
    }
    private void CreatePipeline(IReadOnlyList<Turn>? initial=null)
    {
        if(Demo)demoVad=new(Settings.VadThreshold,Settings.MaxAudioSeconds);
        var current=new Pipeline(Demo?demoApi:new OpenRouter(http,()=>activeKey,Settings,tracker,counters:Counters),Settings,initialHistory:initial,counters:Counters);pipeline=current;
        var session=++visualSession;
        current.Transcribed+=(epoch,item,text)=>Dispatch(()=>
        {
            if(disposed||!ReferenceEquals(pipeline,current)||epoch!=current.Epoch)return;
            sessionTranscripts.Insert(0,new($"{session}-{epoch}-{item}",DateTimeOffset.UtcNow,text,"Recebida",true));
            if(sessionTranscripts.Count>30)sessionTranscripts.RemoveAt(sessionTranscripts.Count-1);
            dashboard?.RefreshData();
        });
        current.Changed+=status=>{var epoch=current.Epoch;Dispatch(()=>{if(!disposed&&ReferenceEquals(pipeline,current)&&epoch==current.Epoch)ReceiveStatus(status);});};
        current.Flow+=notice=>Publish(current,session,notice);
        current.Replied+=turn=>dispatcher.BeginInvoke(()=>
        {
            if(disposed||!ReferenceEquals(pipeline,current)||!pipeline.Enabled||!pipeline.History.Contains(turn))return;
            dashboard?.RefreshData();
            if(dashboard?.DisplaysContent!=true)
                Popover.Open(Orb,pipeline.History,Settings.CloseSeconds,true);
        });
    }
    private void ReceiveStatus(Status status)
    {
        if(recording is not null||changingMode)return;
        lastStatus=status;
        if(processingBlocked&&pipeline.Enabled)
            Orb.SetStatus(new(Phase.Blocked,"A entrada de áudio continua recebendo quadros, mas o filtro local não está processando. Desative e ative o monitoramento para reiniciar o filtro; o histórico foi preservado."));
        else if(captureStatus?.State==CaptureState.Starting&&pipeline.Enabled)
            Orb.SetStatus(new(Phase.Recovering,"Abrindo a entrada de áudio. Aguardando os primeiros quadros para confirmar a escuta."));
        else if(captureStatus?.State==CaptureState.Recovering&&pipeline.Enabled)
            Orb.SetStatus(new(Phase.Recovering,$"A entrada de áudio foi interrompida. Intervalo para nova tentativa: {captureStatus.Delay?.TotalSeconds:0} s (tentativa {captureStatus.Attempt}). Histórico e perguntas em processamento preservados. Clique em Parar no indicador ou no painel para encerrar a escuta."));
        else if(captureStatus?.State==CaptureState.Blocked&&pipeline.Enabled)
            Orb.SetStatus(new(Phase.Blocked,"A entrada de áudio não aceita o formato necessário ou o filtro local está indisponível. Confira o dispositivo padrão e o pré-requisito do filtro; desative e ative o monitoramento após corrigir."));
        else Orb.SetStatus(status);
        if(status.Phase==Phase.Throttled)throttleTimer.Start();else throttleTimer.Stop();
        dashboard?.RefreshControls();
    }
    private void Dispatch(Action action){if(dispatcher.CheckAccess())action();else dispatcher.BeginInvoke(action);}
    private void Publish(Pipeline source,long session,FlowNotice notice)
    {
        // Serialize stamping AND enqueueing: concurrent producers must not overtake STT-start.
        lock(visualDispatch)
        {
            var message=new FlowEvent(notice.Kind,session*1000000+notice.Epoch,++visualSequence,notice.Item,notice.Queued,notice.FilterVerified);
            dispatcher.BeginInvoke(()=>{if(!disposed&&ReferenceEquals(pipeline,source)&&notice.Epoch==source.Epoch)
            {
                Orb.Apply(message);
                var id=$"{session}-{notice.Epoch}-{notice.Item}";var index=sessionTranscripts.FindIndex(t=>t.Id==id);
                var state=notice.Kind switch{FlowKind.JevStarted=>"Classificando",FlowKind.JevIgnored=>"Comentário reconhecido",FlowKind.JevWaiting=>"Aguardando continuação",FlowKind.AnswerStarted=>"Preparando resposta",FlowKind.ResponseReady=>"Resposta concluída",FlowKind.Error=>"Falha no processamento",_=>""};
                if(index>=0&&state.Length>0)
                {
                    sessionTranscripts[index]=sessionTranscripts[index] with{State=state,Active=notice.Kind is FlowKind.JevStarted or FlowKind.AnswerStarted};
                    dashboard?.RefreshData();
                }
            }});
        }
    }
    private void Local(FlowKind kind){if(pipeline.Enabled)Publish(pipeline,visualSession,new(kind,pipeline.Epoch,Queued:pipeline.QueueDepth));}
    public OpenRouter ManagementApi()=>new(http,()=>"",Settings,tracker);
    public string UsageDiagnostic=>tracker.Diagnostic;
    public string CaptureDiagnostic=>Orb.CaptureSummary;
    private void RefreshCaptureHealth()=>Orb.SetCaptureHealth(capture.Health,Interlocked.Read(ref processedFrames),Interlocked.Read(ref processingFaults));
    public void ResumeAfterUpdate()
    {
        if(updateSession is null)return;
        if(updateSession.WasListening&&DependencyStatus.Available)ToggleCapture();else if(!updateSession.WasListening)Store.CompleteUpdateSession();
    }
    public IReadOnlyList<Turn> History=>pipeline.History;
    public IReadOnlyList<RecordingSnapshot> Recordings
    {
        get
        {
            var notice="";
            if(!savedRecordingsLoaded)
            {
                try
                {
                    foreach(var saved in recordings.ReadAll())completedRecordings.TryAdd(saved.Id,saved);
                    savedRecordingsLoaded=true;
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    if(completedRecordings.Count==0&&recording is null)throw;
                    notice="Não foi possível ler todo o histórico do disco.";
                }
            }
            var live=recording?.Snapshot;
            var snapshots=completedRecordings.Values.Where(s=>s.Id!=live?.Id);
            if(live is not null)snapshots=snapshots.Append(live);
            if(notice.Length>0)snapshots=snapshots.Select(s=>s with{Notice=(s.Notice+" "+notice).Trim()});
            return snapshots.OrderByDescending(s=>s.Started).ToArray();
        }
    }
    public IReadOnlyList<SessionTranscript> SessionTranscripts=>sessionTranscripts.ToArray();
    public bool Capturing=>MonitoringEnabled||RecordingActive;
    public bool CanSelectMode=>ModeReady&&!Capturing;
    public bool CanChangeSettings=>ModeReady&&!RecordingActive;
    public string CurrentActivity=>stoppingRecording?"Microfone desligado · finalizando transcrição":
        changingMode?"Preparando captura…":Orb.RecordingLabel.Length>0?Orb.RecordingLabel:
        Orb.CurrentStatus.Phase==Phase.Paused?"Microfone desligado":
        MonitoringEnabled&&Orb.CurrentStatus.Phase==Phase.Listening?"Ouvindo perguntas":Orb.CurrentStatus.Message;
    private string ModePage=>SelectedMode==StudyMode.Questions?"Perguntas":"Transcrições";
    private void OpenPage(string page){Popover.Hide();dashboard??=new DashboardWindow(this);dashboard.Open(page);}
    public void ShowPanel()=>OpenPage(ModePage);
    public void SelectMode(StudyMode mode)
    {
        if(disposed||!CanSelectMode||mode==SelectedMode)return;
        SelectedMode=mode;
        Popover.Hide();
        UpdateControls();
    }
    public void ToggleCapture()
    {
        if(disposed||!ModeReady)return;
        if(SelectedMode==StudyMode.Questions){ToggleQuestions();return;}
        if(RecordingActive)_=StopRecordingAsync();else _=StartRecordingAsync();
    }
    public async Task StartRecordingAsync()
    {
        if(disposed||!CanSelectMode||SelectedMode!=StudyMode.Transcription)return;
        if(!Demo&&(!Store.HasKey||string.IsNullOrWhiteSpace(Settings.SttModel))){ShowSettings();return;}
        changingMode=true;UpdateControls();
        try
        {
            Pause();var version=++modeVersion;Orb.SetRecordingState("Preparando transcrição…");await Task.WhenAll(pipeline.WhenIdle,interruptedRecordingIdle);
            if(disposed||version!=modeVersion)return;
            if(!Demo)activeKey=Store.ReadKey();
            var current=new RecordingSession(recordings,Demo?demoApi:new OpenRouter(http,()=>activeKey,Settings,tracker,counters:Counters),Settings.SttModel);
            current.Changed+=()=>Dispatch(()=>{if(!disposed&&ReferenceEquals(recording,current))RefreshRecording();});
            current.StopRequested+=()=>Dispatch(()=>{if(!disposed&&ReferenceEquals(recording,current))_=StopRecordingAsync();});
            lock(audioLock){recording=current;gate?.Reset();demoVad?.Reset();}
            changingMode=false;RefreshRecording();
            if(dashboard?.IsVisible==true)dashboard.ShowModePage(ModePage,current.Id);
            if(!Demo){healthTimer.Start();capture.Start();}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Pause();Orb.SetStatus(new(Phase.Error,"Não foi possível iniciar a gravação. Confira a chave e o armazenamento local."));
        }
        finally{changingMode=false;if(recording is null)Orb.SetRecordingState("");UpdateControls();}
    }
    public async Task StopRecordingAsync()
    {
        var current=recording;if(current is null||stoppingRecording)return;
        stoppingRecording=true;Orb.SetRecordingState("Finalizando transcrição…");UpdateControls();
        try
        {
            // Close the sole source first. No usual pipeline starts until STT and the final text commit finish.
            healthTimer.Stop();capture.Stop();await current.StopAsync();
            if(disposed||!ReferenceEquals(recording,current))return;
            lock(audioLock){completedRecordings[current.Id]=current.Snapshot;recording=null;current.Dispose();}
            activeKey="";captureStatus=null;Orb.SetRecordingState("");dashboard?.RefreshData();
            stoppingRecording=false;
            lastStatus=new(Phase.Paused,"Microfone desligado");Orb.SetStatus(lastStatus);RefreshCaptureHealth();UpdateTray();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Pause();Orb.SetStatus(new(Phase.Error,"Falha ao salvar a transcrição. Abra Transcrições para copiar o texto disponível."));}
        finally{stoppingRecording=false;UpdateControls();}
    }
    private void UpdateControls()
    {
        controls.Refresh();dashboard?.RefreshControls();
    }
    private void RefreshRecording()
    {
        if(recording is null)return;var snapshot=recording.Snapshot;
        var label=stoppingRecording?"Finalizando transcrição…":RecordingSnapshot.StateLabel(snapshot.State);
        var waiting=captureStatus?.State is CaptureState.Recovering or CaptureState.Starting;
        Orb.SetRecordingState(label+(waiting?" · aguardando entrada de áudio":""),!waiting&&!stoppingRecording&&snapshot.State==RecordingState.Recording);
        if(tray is not null)tray.Text="StudyWhisper · "+label;
        UpdateControls();dashboard?.RefreshData();
    }
    public bool MonitoringEnabled=>pipeline.Enabled;
    public void ShowSettings()=>OpenPage("Configurações");
    public void CheckDependencies()
    {
        Pause();
        try{using var probe=NativeRuntime.Create(NativeRuntime.Check());DependencyStatus=NativeReadiness.Ready;dependencyWindow?.Hide();}
        catch(NativeDependencyException ex){ShowDependencyProblem(ex.Problem);}
        catch{Orb.SetStatus(new(Phase.Error,"O filtro local não inicializou. Reinstale o StudyWhisper. Monitoramento pausado."));}
    }
    private void ShowDependencyProblem(NativeReadiness problem)
    {
        DependencyStatus=problem;Pause();Orb.SetStatus(new(Phase.Error,"Pré-requisito do filtro indisponível. Veja o aviso ou Configurações; monitoramento pausado."));
        dependencyWindow?.Close();dependencyWindow=new DependencyWindow(problem);dependencyWindow.Open(Orb);
    }
    public void Save(Settings s)
    {
        if(recording is not null||changingMode)throw new InvalidOperationException("Pare a gravação antes de alterar as configurações.");
        Pause(); if(!Demo) {Store.Save(s);try{Storage.Startup(s.StartWithWindows);}catch{Store.Save(Settings);throw;}} Settings=s;
        var history=pipeline.History;pipeline.Dispose();CreatePipeline(history);Orb.Configure(s.ReduceMotion);
    }
    private void ToggleQuestions()
    {
        if(disposed||!ModeReady||RecordingActive||SelectedMode!=StudyMode.Questions)return;
        if(!interruptedRecordingIdle.IsCompleted){_=ResumeMonitoringWhenIdleAsync();return;}
        if(pipeline.Enabled){Pause();return;}
        if(Demo){pipeline.Enable();UpdateTray();if(dashboard?.IsVisible==true)dashboard.ShowModePage(ModePage);return;}
        var native=NativeRuntime.Check();if(!native.Available){ShowDependencyProblem(native);return;}
        if(!DependencyStatus.Available){CheckDependencies();if(!DependencyStatus.Available)return;}
        if(!Store.HasKey||Settings.SttModel.Length==0||Settings.JevModel.Length==0||Settings.AnswerModel.Length==0){Orb.SetStatus(new(Phase.Error,"Cadastre sua chave e selecione os três modelos nas configurações"));ShowSettings();return;}
        try
        {
            Settings.Validate();lock(audioLock)gate=new SpeechGate(NativeRuntime.Create(native),Counters,Settings.SpeechProbability,Settings.MaxAudioSeconds);
            gate.Flow+=Local;
            DependencyStatus=NativeReadiness.Ready;dependencyWindow?.Hide();
            activeKey=Store.ReadKey();pipeline.Enable();
            processingBlocked=false;consecutiveFaults=0;captureStatus=null;healthTimer.Start();capture.Start();UpdateTray();
            if(dashboard?.IsVisible==true)dashboard.ShowModePage(ModePage);
        }
        catch(NativeDependencyException ex){ShowDependencyProblem(ex.Problem);}
        catch {Pause();Orb.SetStatus(new(Phase.Error,"Não foi possível iniciar captura ou filtro local. Confira dispositivo, permissões e instalação. Monitoramento pausado."));}
    }
    private async Task ResumeMonitoringWhenIdleAsync()
    {
        changingMode=true;var version=++modeVersion;UpdateControls();
        try{await interruptedRecordingIdle;if(!disposed&&version==modeVersion){changingMode=false;ToggleQuestions();}}
        finally{changingMode=false;UpdateControls();}
    }
    private void UpdateTray()
    {
        UpdateControls();
        if(tray is null)return;
        var state=RecordingActive?"Transcrevendo":!MonitoringEnabled?"Microfone desligado":
            captureStatus?.State==CaptureState.Recovering?"Reconectando entrada de áudio":
            captureStatus?.State==CaptureState.Blocked?"Entrada de áudio indisponível":"Ouvindo perguntas";
        tray.Text="StudyWhisper · "+state;
    }
    private void ResetCaptureState(){lock(audioLock){gate?.Reset();demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}}
    private void OnFrame(short[] frame)
    {
        lock(audioLock)
        {
            if(recording is not null)
            {
                try{recording.Push(frame);Interlocked.Increment(ref processedFrames);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Dispatch(()=>_=StopRecordingAsync());}
                return;
            }
            if(!pipeline.Enabled||processingBlocked)return;
            byte[]? segment=null;
            try
            {
            segment=Demo?demoVad!.Push(frame):gate!.Push(frame)?.Wav;
            var bands=spectrum.Push(frame);
            if(bands.HasValue)
            {
                Volatile.Write(ref latestSpectrum,new(pipeline,pipeline.Epoch,bands.Value));
                if(Interlocked.CompareExchange(ref spectrumPosted,1,0)==0)dispatcher.BeginInvoke(()=>
                {Interlocked.Exchange(ref spectrumPosted,0);var value=Volatile.Read(ref latestSpectrum);if(value is not null&&!disposed&&ReferenceEquals(value.Source,pipeline)&&value.Epoch==pipeline.Epoch&&pipeline.Enabled)Orb.Spectrum(value.Bands);});
            }
            if(segment is not null){if(Demo)Local(FlowKind.LocalApproved);var accepted=segment;segment=null;_=pipeline.SubmitAsync(accepted,!Demo);}
            Interlocked.Increment(ref processedFrames);consecutiveFaults=0;
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                Interlocked.Increment(ref processingFaults);
                var location=new System.Diagnostics.StackTrace(ex,false).GetFrames()?.Select(f=>f.GetMethod()?.DeclaringType?.FullName+"."+f.GetMethod()?.Name).FirstOrDefault(n=>n.StartsWith("StudyWhisper.",StringComparison.Ordinal))??"local";
                consecutiveFaults++;if(!Demo)Store.RecordCaptureFault("Processing",consecutiveFaults,"filtro-local",0,ex.GetType().Name+"@"+location);
                processingBlocked=consecutiveFaults>=3||ex is NativeDependencyException;
                if(!processingBlocked){try{ResetCaptureState();}catch{processingBlocked=true;}}
                // A local filter failure must not close/reopen the Windows capture or replay a paid stage.
                var epoch=pipeline.Epoch;Dispatch(()=>{if(!disposed&&pipeline.Enabled&&pipeline.Epoch==epoch){RefreshCaptureHealth();ReceiveStatus(lastStatus);}});
            }
            finally{if(segment is not null)Array.Clear(segment);}
        }
    }
    public void Pause()
    {
        modeVersion++;
        for(var index=0;index<sessionTranscripts.Count;index++)if(sessionTranscripts[index].Active)sessionTranscripts[index]=sessionTranscripts[index] with{State="Interrompida",Active=false};
        if(recording is not null){var interrupted=recording;lock(audioLock){recording=null;interrupted.Dispose();interruptedRecordingIdle=interrupted.WhenIdle;completedRecordings[interrupted.Id]=interrupted.Snapshot;}Orb.SetRecordingState("");dashboard?.RefreshData();}
        healthTimer.Stop();pipeline.Pause();capture.Stop();captureStatus=null;processingBlocked=false;activeKey="";lock(audioLock){gate?.Dispose();gate=null;demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}lastStatus=new(Phase.Paused,"Microfone desligado");Orb.SetStatus(lastStatus);RefreshCaptureHealth();UpdateTray();
    }
    public void Clear()
    {
        pipeline.Clear();sessionTranscripts.Clear();dashboard?.RefreshData();lock(audioLock){gate?.Reset();demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}Popover.Hide();
    }
    public void ToggleHistory() {if(Popover.IsVisible)Popover.Hide();else Popover.Open(Orb,pipeline.History,Settings.CloseSeconds);}
    public async Task SimulateAsync(string transcript)
    {
        if(!Demo)throw new InvalidOperationException();
        if(!ModeReady||(!RecordingActive&&SelectedMode!=StudyMode.Questions))return;
        demoApi.Transcript=transcript;if(!Capturing)ToggleCapture();Local(FlowKind.Candidate);UpdateTray();
        foreach(var frame in Wav.Synthetic()) {OnFrame(frame);Array.Clear(frame);await Task.Delay(20);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;throttleTimer.Stop();Pause();capture.Dispose();pipeline.Dispose();trayDismissal?.Dispose();tray?.Dispose();Popover.Close();dashboard?.Destroy();dependencyWindow?.Close();Orb.Close();http.Dispose();
    }
}
