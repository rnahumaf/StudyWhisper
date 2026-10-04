namespace StudyWhisper.App;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(args.Contains("--audio-smoke"))return AudioSmoke.Run(args);
        if(args.Contains("--dependency-smoke"))return DependencySmoke.Run(args);
        var smoke=args.Contains("--ui-smoke");var demo=smoke||args.Contains("--demo");
        using var mutex=new Mutex(true,smoke?"Local\\StudyWhisper-Smoke-"+Guid.NewGuid():demo?"Local\\StudyWhisper-Demo":"Local\\StudyWhisper",out var first);if(!first)return 0;
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Controller? controller=null;
        app.Startup+=async(_,_)=>
        {
            controller=new Controller(demo,smoke);
            if(smoke) {await UiSmoke.RunAsync(controller,args);app.Shutdown();return;}
            controller.Orb.Show();
            if(!demo)controller.CheckDependencies();
            if(demo)
            {
                var preview=new Window{Title="StudyWhisper • Demonstração",Width=450,Height=430,WindowStartupLocation=WindowStartupLocation.CenterScreen};Ui.Style(preview);var p=new StackPanel{Margin=new Thickness(20)};preview.Content=p;
                p.Children.Add(Ui.Text("Demonstração offline",20));p.Children.Add(Ui.Text("Sem microfone, chave ou conexão. O som é sintético e a transcrição é uma fixture.",14,Ui.Muted));
                p.Children.Add(Ui.Button("Simular pergunta",()=>_=controller.SimulateAsync("Qual é a diferença entre mitose e meiose?")));
                p.Children.Add(Ui.Button("Simular comentário",()=>_=controller.SimulateAsync("Entendi, interessante.")));
                p.Children.Add(Ui.Button("Configurações",controller.ShowSettings));p.Children.Add(Ui.Button("Simular fala incompleta",()=>_=controller.SimulateAsync("Qual a diferença entre")));p.Children.Add(Ui.Button("Pausar / cancelar",controller.Pause));p.Children.Add(Ui.Button("Limpar sessão",controller.Clear));preview.Show();
            }
        };
        app.Exit+=(_,_)=>controller?.Dispose();
        app.DispatcherUnhandledException+=(_,e)=>{e.Handled=true;controller?.Pause();controller?.Orb.SetStatus(new(Phase.Error,"Falha no app. Monitoramento pausado; reinicie para continuar."));if(controller is null)app.Shutdown(1);};
        return Math.Max(app.Run(),Environment.ExitCode);
    }
}
