using System.Net.Http;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace StudyWhisper.App;

public sealed class Controller : IDisposable
{
    public Storage Store {get;}
    public Settings Settings {get;private set;}
    public bool Demo {get;}
    public SessionCounters Counters {get;}=new();
    private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
    private readonly CallTracker tracker;
    private readonly Forms.NotifyIcon? tray;
    private readonly Forms.ToolStripMenuItem? toggle;
    public OrbWindow Orb {get;}
    public PopoverWindow Popover {get;}
    public OrbToolsWindow Tools {get;}
    private RewriteWindow? rewriteWindow;
    private Pipeline pipeline=null!;
    private readonly CaptureRecovery capture;
    private readonly UpdateSession? updateSession;
    private readonly DispatcherTimer healthTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private long processedFrames,processingFaults;
    private int consecutiveFaults;
    private volatile bool processingBlocked;
    private CaptureStatus? captureStatus;
    private Status lastStatus=new(Phase.Paused,"Monitoramento desativado");
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
    private SettingsWindow? window;
    private DependencyWindow? dependencyWindow;
    public NativeReadiness DependencyStatus {get;private set;}=NativeReadiness.Ready;
    private readonly DispatcherTimer throttleTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private string activeKey="";
    private readonly DemoApi demoApi=new();
    private bool disposed;
    private readonly RecordingJournal recordings;
    private RecordingSession? recording;
    private RecordingSnapshot? lastRecording;
    private readonly Dictionary<string,RecordingSnapshot> completedRecordings=new();
    private long modeVersion;
    private Task interruptedRecordingIdle=Task.CompletedTask;
    private RecordingsWindow? recordingsWindow;
    private bool changingMode,stoppingRecording,resumeMonitoring;
    private readonly Forms.ToolStripMenuItem? recordMenu;
    public string RecordAction=>changingMode?"Preparando…":stoppingRecording?"Parando…":recording is null?"Gravar":"Parar";
    private readonly Dispatcher dispatcher=System.Windows.Application.Current.Dispatcher;
    public Controller(bool demo,bool smoke=false,string? dataRoot=null)
    {
        Demo=demo;Store=new Storage(dataRoot??(demo?Path.Combine(Path.GetTempPath(),"StudyWhisper-demo-unused"):Storage.DefaultRoot));Settings=demo?new Settings{AnswerModel="demo/offline"}:Store.Load();tracker=new();
        recordings=new RecordingJournal(Path.Combine(dataRoot??(smoke?Path.Combine(Path.GetTempPath(),"StudyWhisper-recording-smoke-"+Guid.NewGuid()):demo?Path.Combine(Path.GetTempPath(),"StudyWhisper-recording-demo"):Storage.DefaultRoot),"recordings"));
        try{recordings.Recover();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){lastStatus=new(Phase.Error,"Não foi possível recuperar o histórico de gravações. Confira o armazenamento local.");}
        updateSession=demo||smoke?null:Store.ReadUpdateSession();if(updateSession is not null)tracker.RestoreUsedForUpdate(updateSession.UsedCalls);if(updateSession?.CounterState is not null)Counters.Restore(updateSession.CounterState);
        Orb=new OrbWindow(ToggleHistory,ShowSettings,ToggleMonitoringFromOrb,Clear,()=>System.Windows.Application.Current.Shutdown(),ToggleRecording,ShowRecordings,()=>RecordAction,ShowRewrite);Popover=new PopoverWindow(Clear);Tools=new OrbToolsWindow(Orb,ToggleRecording,ShowRewrite);
        Orb.Configure(Settings.ReduceMotion);
        CreatePipeline(updateSession?.History);
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
                    if(state.State==CaptureState.Recovering)try{recording.CaptureGap();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){_=StopRecordingAsync(false);}
                    if(state.State==CaptureState.Blocked){recording.CaptureGap();_=StopRecordingAsync(false);}
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
            tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="StudyWhisper • Monitoramento desativado",Visible=true};
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("Configurações",null,(_,_)=>ShowSettings());toggle=new Forms.ToolStripMenuItem("Monitoramento desativado",null,(_,_)=>Toggle());menu.Items.Add(toggle);
            recordMenu=new Forms.ToolStripMenuItem("Gravar",null,(_,_)=>ToggleRecording());menu.Items.Insert(0,recordMenu);menu.Items.Insert(1,new Forms.ToolStripMenuItem("Gravações",null,(_,_)=>ShowRecordings()));
            menu.Items.Insert(2,new Forms.ToolStripMenuItem("Rewrite",null,(_,_)=>ShowRewrite()));
            menu.Items.Add("Histórico da sessão",null,(_,_)=>ToggleHistory());menu.Items.Add("Limpar memória e histórico",null,(_,_)=>Clear());menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Sair",null,(_,_)=>System.Windows.Application.Current.Shutdown());
            menu.AutoClose=true;
            menu.Opened+=(_,_)=>{trayDismissal?.Dispose();trayDismissal=new(()=>new[]{menu.Handle},()=>menu.Close());};
            menu.Closed+=(_,_)=>{trayDismissal?.Dispose();trayDismissal=null;};
            tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ToggleHistory();
        }
    }
    private void CreatePipeline(IReadOnlyList<Turn>? initial=null)
    {
        if(Demo)demoVad=new(Settings.VadThreshold,Settings.MaxAudioSeconds);
        var current=new Pipeline(Demo?demoApi:new OpenRouter(http,()=>activeKey,Settings,tracker,counters:Counters),Settings,initialHistory:initial,counters:Counters);pipeline=current;
        var session=++visualSession;
        current.Changed+=status=>{var epoch=current.Epoch;Dispatch(()=>{if(!disposed&&ReferenceEquals(pipeline,current)&&epoch==current.Epoch)ReceiveStatus(status);});};
        current.Flow+=notice=>Publish(current,session,notice);
        current.Replied+=turn=>dispatcher.BeginInvoke(()=>{if(!disposed&&ReferenceEquals(pipeline,current)&&pipeline.Enabled&&pipeline.History.Contains(turn))Popover.Open(Orb,pipeline.History,Settings.CloseSeconds,true);});
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
            Orb.SetStatus(new(Phase.Recovering,$"A entrada de áudio foi interrompida. Intervalo para nova tentativa: {captureStatus.Delay?.TotalSeconds:0} s (tentativa {captureStatus.Attempt}). Histórico e perguntas em processamento preservados. Use o menu para pausar."));
        else if(captureStatus?.State==CaptureState.Blocked&&pipeline.Enabled)
            Orb.SetStatus(new(Phase.Blocked,"A entrada de áudio não aceita o formato necessário ou o filtro local está indisponível. Confira o dispositivo padrão e o pré-requisito do filtro; desative e ative o monitoramento após corrigir."));
        else Orb.SetStatus(status);
        if(status.Phase==Phase.Throttled)throttleTimer.Start();else throttleTimer.Stop();
    }
    private void Dispatch(Action action){if(dispatcher.CheckAccess())action();else dispatcher.BeginInvoke(action);}
    private void Publish(Pipeline source,long session,FlowNotice notice)
    {
        // Serialize stamping AND enqueueing: concurrent producers must not overtake STT-start.
        lock(visualDispatch)
        {
            var message=new FlowEvent(notice.Kind,session*1000000+notice.Epoch,++visualSequence,notice.Item,notice.Queued,notice.FilterVerified);
            dispatcher.BeginInvoke(()=>{if(!disposed&&ReferenceEquals(pipeline,source)&&notice.Epoch==source.Epoch)Orb.Apply(message);});
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
        if(updateSession.WasListening&&DependencyStatus.Available)Toggle();else if(!updateSession.WasListening)Store.CompleteUpdateSession();
    }
    public IReadOnlyList<Turn> History=>pipeline.History;
    public IReadOnlyList<RecordingSnapshot> Recordings
    {
        get
        {
            List<RecordingSnapshot> all;var readNotice="";
            try{all=recordings.ReadAll().ToList();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){all=new();readNotice="Não foi possível ler o disco. Esta cópia em memória pode ser copiada enquanto o app estiver aberto.";}
            foreach(var saved in completedRecordings.Values){all.RemoveAll(s=>s.Id==saved.Id);all.Add(saved);}var live=recording?.Snapshot??lastRecording;
            if(live is not null){all.RemoveAll(s=>s.Id==live.Id);all.Add(live);}
            if(readNotice.Length>0){if(all.Count==0)throw new IOException("Armazenamento indisponível.");all=all.Select(s=>s with{Notice=s.Notice+" "+readNotice}).ToList();}
            return all.OrderByDescending(s=>s.Started).ToArray();
        }
    }
    public void ShowRecordings(){rewriteWindow?.Hide();Popover.Hide();recordingsWindow??=new RecordingsWindow(()=>Recordings);recordingsWindow.Open(recording?.Id,Orb);}
    public void ShowRewrite(){Popover.Hide();recordingsWindow?.Hide();rewriteWindow??=new RewriteWindow(Orb,RewriteAsync);rewriteWindow.Open();}
    private async Task<string> RewriteAsync(string text,CancellationToken token)
    {
        if(Demo){await Task.Delay(350,token);return text;}
        if(!Store.HasKey)throw new ApiException("Cadastre sua chave nas configurações antes de revisar.");
        var secret=Store.ReadKey();try{return await new OpenRouter(http,()=>secret,Settings,tracker,counters:Counters).RewriteAsync(text,token);}finally{secret="";}
    }
    public void ToggleRecording(){if(changingMode||stoppingRecording)return;if(recording is null)_=StartRecordingAsync();else _=StopRecordingAsync(true);}
    public async Task StartRecordingAsync()
    {
        if(disposed||changingMode||recording is not null)return;
        if(!Demo&&!Store.HasKey){ShowSettings();return;}
        changingMode=true;resumeMonitoring=pipeline.Enabled;window?.Hide();UpdateRecordingMenu();
        try
        {
            Pause();var version=++modeVersion;Orb.SetRecordingState("Preparando gravação…");await Task.WhenAll(pipeline.WhenIdle,interruptedRecordingIdle);
            if(disposed||version!=modeVersion)return;
            if(!Demo)activeKey=Store.ReadKey();
            var current=new RecordingSession(recordings,Demo?demoApi:new OpenRouter(http,()=>activeKey,Settings,tracker,counters:Counters),Settings.SttModel);
            current.Changed+=()=>Dispatch(()=>{if(!disposed&&ReferenceEquals(recording,current))RefreshRecording();});
            current.StopRequested+=()=>Dispatch(()=>{if(!disposed&&ReferenceEquals(recording,current))_=StopRecordingAsync(true);});
            lock(audioLock){recording=current;gate?.Reset();demoVad?.Reset();}
            changingMode=false;RefreshRecording();ShowRecordings();
            if(!Demo){healthTimer.Start();capture.Start();}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Pause();Orb.SetStatus(new(Phase.Error,"Não foi possível iniciar a gravação. Confira a chave e o armazenamento local."));
        }
        finally{changingMode=false;if(recording is null)Orb.SetRecordingState("");UpdateRecordingMenu();}
    }
    public async Task StopRecordingAsync(bool restore=true)
    {
        var current=recording;if(current is null||stoppingRecording)return;
        stoppingRecording=true;UpdateRecordingMenu();Orb.SetRecordingState("Parando · concluindo transcrições pendentes…");
        try
        {
            // Close the sole source first. No usual pipeline starts until STT and the final text commit finish.
            healthTimer.Stop();capture.Stop();await current.StopAsync();
            if(disposed||!ReferenceEquals(recording,current))return;
            lock(audioLock){lastRecording=current.Snapshot;completedRecordings[lastRecording.Id]=lastRecording;recording=null;current.Dispose();}
            activeKey="";captureStatus=null;Orb.SetRecordingState("");recordingsWindow?.Refresh();
            stoppingRecording=false;
            if(restore&&resumeMonitoring)Toggle();else{lastStatus=new(Phase.Paused,"Monitoramento desativado");Orb.SetStatus(lastStatus);RefreshCaptureHealth();UpdateTray();}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Pause();Orb.SetStatus(new(Phase.Error,"Gravação interrompida por falha ao salvar. Abra Gravações para copiar o texto disponível."));}
        finally{stoppingRecording=false;UpdateRecordingMenu();}
    }
    private void UpdateRecordingMenu()
    {
        Tools.SetState(RecordAction);Orb.SetMonitoringState(pipeline.Enabled,!changingMode&&!stoppingRecording&&recording is null);
        if(recordMenu is not null){recordMenu.Text=RecordAction;recordMenu.Enabled=!changingMode&&!stoppingRecording;}
        if(toggle is not null)toggle.Enabled=!changingMode&&!stoppingRecording&&recording is null;
    }
    private void RefreshRecording()
    {
        if(recording is null)return;var snapshot=recording.Snapshot;
        var label=RecordingSnapshot.StateLabel(snapshot.State);
        Orb.SetRecordingState(label+(captureStatus?.State is CaptureState.Recovering or CaptureState.Starting?" · aguardando entrada de áudio":""));
        if(tray is not null)tray.Text="StudyWhisper · "+label;
        UpdateRecordingMenu();recordingsWindow?.Refresh();
    }
    public bool MonitoringEnabled=>pipeline.Enabled;
    public void ShowSettings() {if(recording is not null||changingMode){ShowRecordings();return;}if(window is null){window=new SettingsWindow(this);window.Closed+=(_,_)=>window=null;}window.Show();window.Activate();}
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
    internal void ToggleMonitoringFromOrb(){if(!disposed&&!changingMode&&!stoppingRecording&&recording is null)Toggle();}
    public void Toggle()
    {
        if(changingMode)return;
        if(recording is not null){_=StopRecordingAsync(false);return;}
        if(!interruptedRecordingIdle.IsCompleted){_=ResumeMonitoringWhenIdleAsync();return;}
        if(pipeline.Enabled){Pause();return;}
        if(Demo){pipeline.Enable();UpdateTray();return;}
        var native=NativeRuntime.Check();if(!native.Available){ShowDependencyProblem(native);return;}
        if(!DependencyStatus.Available){CheckDependencies();if(!DependencyStatus.Available)return;}
        if(!Store.HasKey||Settings.AnswerModel.Length==0){Orb.SetStatus(new(Phase.Error,"Cadastre sua chave e selecione os três modelos nas configurações"));ShowSettings();return;}
        try
        {
            Settings.Validate();lock(audioLock)gate=new SpeechGate(NativeRuntime.Create(native),Counters,Settings.SpeechProbability,Settings.MaxAudioSeconds);
            gate.Flow+=Local;
            DependencyStatus=NativeReadiness.Ready;dependencyWindow?.Hide();
            activeKey=Store.ReadKey();pipeline.Enable();
            processingBlocked=false;consecutiveFaults=0;captureStatus=null;healthTimer.Start();capture.Start();UpdateTray();
        }
        catch(NativeDependencyException ex){ShowDependencyProblem(ex.Problem);}
        catch {Pause();Orb.SetStatus(new(Phase.Error,"Não foi possível iniciar captura ou filtro local. Confira dispositivo, permissões e instalação. Monitoramento pausado."));}
    }
    private async Task ResumeMonitoringWhenIdleAsync()
    {
        changingMode=true;var version=++modeVersion;UpdateRecordingMenu();
        try{await interruptedRecordingIdle;if(!disposed&&version==modeVersion){changingMode=false;Toggle();}}
        finally{changingMode=false;UpdateRecordingMenu();}
    }
    private void UpdateTray() {Orb.SetMonitoringState(pipeline.Enabled,!changingMode&&!stoppingRecording&&recording is null);var state=!pipeline.Enabled?"Monitoramento desativado":captureStatus?.State==CaptureState.Recovering?"Reconectando entrada de áudio":captureStatus?.State==CaptureState.Blocked?"Entrada de áudio indisponível":"Monitoramento ativado";if(toggle is not null){toggle.Text=state;toggle.Checked=pipeline.Enabled;}if(tray is not null)tray.Text="StudyWhisper • "+state;}
    private void ResetCaptureState(){lock(audioLock){gate?.Reset();demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}}
    private void OnFrame(short[] frame)
    {
        lock(audioLock)
        {
            if(recording is not null)
            {
                try{recording.Push(frame);Interlocked.Increment(ref processedFrames);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Dispatch(()=>_=StopRecordingAsync(false));}
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
        if(recording is not null){var interrupted=recording;lock(audioLock){recording=null;interrupted.Dispose();interruptedRecordingIdle=interrupted.WhenIdle;lastRecording=interrupted.Snapshot;completedRecordings[lastRecording.Id]=lastRecording;}Orb.SetRecordingState("");recordingsWindow?.Refresh();}
        healthTimer.Stop();pipeline.Pause();capture.Stop();captureStatus=null;processingBlocked=false;activeKey="";lock(audioLock){gate?.Dispose();gate=null;demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}lastStatus=new(Phase.Paused,"Monitoramento desativado");Orb.SetStatus(lastStatus);RefreshCaptureHealth();UpdateTray();
    }
    public void Clear()
    {
        pipeline.Clear();lock(audioLock){gate?.Reset();demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}Popover.Hide();
    }
    public void ToggleHistory() {if(Popover.IsVisible)Popover.Hide();else Popover.Open(Orb,pipeline.History,Settings.CloseSeconds);}
    public async Task SimulateAsync(string transcript)
    {
        if(!Demo)throw new InvalidOperationException();demoApi.Transcript=transcript;if(!pipeline.Enabled&&recording is null)pipeline.Enable();Local(FlowKind.Candidate);UpdateTray();
        foreach(var frame in Wav.Synthetic()) {OnFrame(frame);Array.Clear(frame);await Task.Delay(20);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;throttleTimer.Stop();Pause();capture.Dispose();pipeline.Dispose();trayDismissal?.Dispose();tray?.Dispose();Popover.Close();recordingsWindow?.Destroy();rewriteWindow?.Destroy();dependencyWindow?.Close();Tools.Close();Orb.Close();window?.Close();http.Dispose();
    }
}
