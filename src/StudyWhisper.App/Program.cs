namespace StudyWhisper.App;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(args.Contains("--audio-smoke"))return AudioSmoke.Run(args);
        if(args.Contains("--dependency-smoke"))return DependencySmoke.Run(args);
        var smoke=args.Contains("--ui-smoke");var demo=smoke||args.Contains("--demo");
        using var mutex=new Mutex(true,smoke?"Local\\StudyWhisper-Smoke-"+Guid.NewGuid():demo?"Local\\StudyWhisper-Demo":"Local\\StudyWhisper",out var first);
        var activationName=demo?"Local\\StudyWhisper-Demo-Activate":"Local\\StudyWhisper-Activate";
        if(!first)
        {
            if(!args.Contains("--startup"))
                for(var attempt=0;attempt<20;attempt++)
                {
                    try {using var existing=EventWaitHandle.OpenExisting(activationName);existing.Set();break;}
                    catch(WaitHandleCannotBeOpenedException){Thread.Sleep(100);}
                    catch(UnauthorizedAccessException){break;}
                }
            return 0;
        }
        using var activation=smoke?null:new EventWaitHandle(false,EventResetMode.AutoReset,activationName);
        RegisteredWaitHandle? activationWait=null;
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Controller? controller=null;
        app.Startup+=async(_,_)=>
        {
            controller=new Controller(demo,smoke);
            if(smoke) {await UiSmoke.RunAsync(controller,args);app.Shutdown();return;}
            activationWait=ThreadPool.RegisterWaitForSingleObject(activation!,(_,_)=>app.Dispatcher.BeginInvoke(()=>controller?.ShowPanel()),null,Timeout.Infinite,false);
            controller.Orb.Show();
            if(!demo){controller.CheckDependencies();controller.ResumeAfterUpdate();}
            if(demo)
            {
                var preview=new Window{Title="StudyWhisper • Demonstração",Width=450,Height=430,WindowStartupLocation=WindowStartupLocation.CenterScreen};Ui.Style(preview);var p=new StackPanel{Margin=new Thickness(20)};preview.Content=p;
                p.Children.Add(Ui.Text("Demonstração offline",20));p.Children.Add(Ui.Text("Sem microfone, chave ou conexão. O som é sintético e a transcrição é uma fixture.",14,Ui.Muted));
                p.Children.Add(Ui.Button("Simular pergunta",()=>_=controller.SimulateAsync("Qual é a diferença entre mitose e meiose?")));
                p.Children.Add(Ui.Button("Simular comentário",()=>_=controller.SimulateAsync("Entendi, interessante.")));
                p.Children.Add(Ui.Button("Configurações",controller.ShowSettings));p.Children.Add(Ui.Button("Simular fala incompleta",()=>_=controller.SimulateAsync("Qual a diferença entre")));p.Children.Add(Ui.Button("Pausar / cancelar",controller.Pause));p.Children.Add(Ui.Button("Limpar sessão",controller.Clear));preview.Show();
            }
        };
        app.Exit+=(_,_)=>{activationWait?.Unregister(null);controller?.Dispose();};
        app.DispatcherUnhandledException+=(_,e)=>{e.Handled=true;controller?.Pause();controller?.Orb.SetStatus(new(Phase.Error,"Falha no app. Monitoramento pausado; reinicie para continuar."));if(controller is null)app.Shutdown(1);};
        return Math.Max(app.Run(),Environment.ExitCode);
    }
}
