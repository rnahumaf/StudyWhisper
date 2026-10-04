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
    private readonly CallBudget budget;
    private readonly Forms.NotifyIcon? tray;
    private readonly Forms.ToolStripMenuItem? toggle;
    public OrbWindow Orb {get;}
    public PopoverWindow Popover {get;}
    private Pipeline pipeline=null!;
    private Microphone? microphone;
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
    private readonly Dispatcher dispatcher=System.Windows.Application.Current.Dispatcher;
    public Controller(bool demo,bool smoke=false)
    {
        Demo=demo;Store=new Storage(demo?Path.Combine(Path.GetTempPath(),"StudyWhisper-demo-unused"):Storage.DefaultRoot);Settings=demo?new Settings{AnswerModel="demo/offline"}:Store.Load();budget=new(Settings.CallsPerMinute,Settings.SessionCallLimit);
        Orb=new OrbWindow(ToggleHistory,ShowSettings,Toggle,Clear,()=>System.Windows.Application.Current.Shutdown());Popover=new PopoverWindow(Clear);
        Orb.Configure(Settings.ReduceMotion);
        CreatePipeline();
        throttleTimer.Tick+=(_,_)=>
        {
            if(disposed||!pipeline.Enabled){throttleTimer.Stop();return;}
            try{budget.EnsureAvailable(3);throttleTimer.Stop();Orb.EndThrottle();}
            catch(ThrottleException ex){Orb.SetStatus(new(Phase.Throttled,ex.Message));if(ex.RetryAfter is null)throttleTimer.Stop();}
        };
        if(!smoke)
        {
            tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="StudyWhisper • Monitoramento desativado",Visible=true};
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("Configurações",null,(_,_)=>ShowSettings());toggle=new Forms.ToolStripMenuItem("Monitoramento desativado",null,(_,_)=>Toggle());menu.Items.Add(toggle);
            menu.Items.Add("Histórico da sessão",null,(_,_)=>ToggleHistory());menu.Items.Add("Limpar memória e histórico",null,(_,_)=>Clear());menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Sair",null,(_,_)=>System.Windows.Application.Current.Shutdown());tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ToggleHistory();
        }
    }
    private void CreatePipeline(IReadOnlyList<Turn>? initial=null)
    {
        if(Demo)demoVad=new(Settings.VadThreshold,Settings.MaxAudioSeconds);
        var current=new Pipeline(Demo?demoApi:new OpenRouter(http,()=>activeKey,Settings,budget,counters:Counters),Settings,initialHistory:initial,counters:Counters);pipeline=current;
        var session=++visualSession;
        current.Changed+=status=>{var epoch=current.Epoch;Dispatch(()=>{if(!disposed&&ReferenceEquals(pipeline,current)&&epoch==current.Epoch)ReceiveStatus(status);});};
        current.Flow+=notice=>Publish(current,session,notice);
        current.Replied+=turn=>dispatcher.BeginInvoke(()=>{if(!disposed&&ReferenceEquals(pipeline,current)&&pipeline.Enabled&&pipeline.History.Contains(turn))Popover.Open(Orb,pipeline.History,Settings.CloseSeconds,true);});
    }
    private void ReceiveStatus(Status status)
    {
        Orb.SetStatus(status);if(status.Phase==Phase.Throttled)throttleTimer.Start();else throttleTimer.Stop();
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
    public OpenRouter ManagementApi()=>new(http,()=>"",Settings,budget);
    public string BudgetDiagnostic=>budget.Diagnostic;
    public IReadOnlyList<Turn> History=>pipeline.History;
    public void ShowSettings() {if(window is null){window=new SettingsWindow(this);window.Closed+=(_,_)=>window=null;}window.Show();window.Activate();}
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
        Pause(); if(!Demo) {Store.Save(s);try{Storage.Startup(s.StartWithWindows);}catch{Store.Save(Settings);throw;}} Settings=s;budget.Configure(s.CallsPerMinute,s.SessionCallLimit);
        var history=pipeline.History;pipeline.Dispose();CreatePipeline(history);Orb.Configure(s.ReduceMotion);
    }
    public void Toggle()
    {
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
            microphone=new Microphone();microphone.Frame+=OnFrame;microphone.Failed+=()=>dispatcher.BeginInvoke(()=>{Pause();Orb.SetStatus(new(Phase.Error,"A captura de áudio falhou. Monitoramento pausado."));});microphone.Start();UpdateTray();
        }
        catch(NativeDependencyException ex){ShowDependencyProblem(ex.Problem);}
        catch {Pause();Orb.SetStatus(new(Phase.Error,"Não foi possível iniciar captura ou filtro local. Confira dispositivo, permissões e instalação. Monitoramento pausado."));}
    }
    private void UpdateTray() {if(toggle is not null){toggle.Text=pipeline.Enabled?"Monitoramento ativado":"Monitoramento desativado";toggle.Checked=pipeline.Enabled;}if(tray is not null)tray.Text="StudyWhisper • "+(pipeline.Enabled?"Monitoramento ativado":"Monitoramento desativado");}
    private void OnFrame(short[] frame)
    {
        lock(audioLock)
        {
            if(!pipeline.Enabled)return;
            var segment=Demo?demoVad!.Push(frame):gate!.Push(frame)?.Wav;
            var bands=spectrum.Push(frame);
            if(bands.HasValue)
            {
                Volatile.Write(ref latestSpectrum,new(pipeline,pipeline.Epoch,bands.Value));
                if(Interlocked.CompareExchange(ref spectrumPosted,1,0)==0)dispatcher.BeginInvoke(()=>
                {Interlocked.Exchange(ref spectrumPosted,0);var value=Volatile.Read(ref latestSpectrum);if(value is not null&&!disposed&&ReferenceEquals(value.Source,pipeline)&&value.Epoch==pipeline.Epoch&&pipeline.Enabled)Orb.Spectrum(value.Bands);});
            }
            if(segment is not null){if(Demo)Local(FlowKind.LocalApproved);_=pipeline.SubmitAsync(segment,!Demo);}
        }
    }
    public void Pause()
    {
        pipeline.Pause();microphone?.Dispose();microphone=null;activeKey="";lock(audioLock){gate?.Dispose();gate=null;demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}Orb.SetStatus(new(Phase.Paused,"Monitoramento desativado"));UpdateTray();
    }
    public void Clear()
    {
        pipeline.Clear();lock(audioLock){gate?.Reset();demoVad?.Reset();spectrum.Reset();Volatile.Write(ref latestSpectrum,null);}Popover.Hide();
    }
    public void ToggleHistory() {if(Popover.IsVisible)Popover.Hide();else Popover.Open(Orb,pipeline.History,Settings.CloseSeconds);}
    public async Task SimulateAsync(string transcript)
    {
        if(!Demo)throw new InvalidOperationException();demoApi.Transcript=transcript;if(!pipeline.Enabled)pipeline.Enable();Local(FlowKind.Candidate);UpdateTray();
        foreach(var frame in Wav.Synthetic()) {OnFrame(frame);Array.Clear(frame);await Task.Delay(20);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;throttleTimer.Stop();Pause();pipeline.Dispose();tray?.Dispose();Popover.Close();dependencyWindow?.Close();Orb.Close();window?.Close();http.Dispose();
    }
}
