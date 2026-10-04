using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation;
namespace StudyWhisper.App;

/// <summary>Render actual native WPF surfaces with fixtures; no OS input injection, microphone, secrets, or HTTP.</summary>
public static class UiSmoke
{
    public static async Task RunAsync(Controller c,string[] args)
    {
        var i=Array.IndexOf(args,"--output");var dir=i>=0&&i+1<args.Length?args[i+1]:Path.Combine(AppContext.BaseDirectory,"ui-smoke");Directory.CreateDirectory(dir);
        var checks=new List<object>();
        void Check(string name,bool passed) {checks.Add(new{name,passed});if(!passed)throw new InvalidOperationException("UI smoke: "+name);}
        try
        {
            c.Orb.Left=SystemParameters.WorkArea.Left+20;
            var foreground=PassiveWindow.GetForegroundWindow();c.Orb.Show();await Yield();
            Check("Orb does not take foreground focus",PassiveWindow.GetForegroundWindow()==foreground);
            Check("Orb has WS_EX_NOACTIVATE",c.Orb.NoActivateVerified);Capture(c.Orb,Path.Combine(dir,"orb-paused.png"));
            var opened=new List<Uri>();var dependency=new DependencyWindow(NativeRuntime.Check(_=>false),opened.Add);dependency.Open(c.Orb);await Yield();
            Check("Missing dependency notice does not take foreground focus",PassiveWindow.GetForegroundWindow()==foreground&&dependency.NoActivateVerified);
            Check("Missing dependency has actionable x64 advice and reachable help",dependency.Explanation.Text.Contains("Microsoft Visual C++")&&dependency.Explanation.Text.Contains("x64")&&dependency.HelpButton.IsVisible&&dependency.HelpButton.ActualHeight>=34);
            Capture(dependency,Path.Combine(dir,"dependency-missing.png"));dependency.Width=360;await Yield();Capture(dependency,Path.Combine(dir,"dependency-missing-narrow.png"));
            Check("Dependency notice wraps at narrow width",dependency.ActualWidth==360&&dependency.Explanation.ActualWidth<=324&&dependency.HelpButton.ActualWidth<=324);
            dependency.HelpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Help uses official URI without opening browser in fixtures",opened.SequenceEqual(new[]{NativeRuntime.HelpUri}));
            dependency.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Dependency notice closes without enabling monitoring",!dependency.IsVisible&&!c.History.Any());dependency.Close();
            c.ToggleHistory();await Yield();Check("History opens without stealing focus",PassiveWindow.GetForegroundWindow()==foreground);
            Check("Popover has WS_EX_NOACTIVATE",c.Popover.NoActivateVerified);Capture(c.Popover,Path.Combine(dir,"history-empty.png"));c.Popover.Hide();
            c.Counters.Monitor(16000*120);c.Counters.Speech(16000*12);c.Counters.RejectLocal();c.Counters.Call("audio/transcriptions",14);c.Counters.Call("decisions");
            var settings=new SettingsWindow(c){ShowActivated=false};settings.Show();await Yield();Capture(settings,Path.Combine(dir,"settings-normal.png"));
            Check("Session usage shows unknown cost and counted STT seconds",settings.Usage.Text.Contains("desconhecido")&&settings.Usage.Text.Contains("14"));
            Check("Settings content scrolls and footer remains reachable",settings.Scroller.ScrollableHeight>0&&settings.ActualWidth==660);
            settings.Height=450;settings.Width=580;await Yield();Capture(settings,Path.Combine(dir,"settings-minimum.png"));
            settings.Usage.BringIntoView();await Yield();Capture(settings,Path.Combine(dir,"settings-usage.png"));
            settings.Scroller.ScrollToBottom();await Yield();Capture(settings,Path.Combine(dir,"settings-minimum-bottom.png"));Check("Settings scroll reaches limits",Math.Abs(settings.Scroller.VerticalOffset-settings.Scroller.ScrollableHeight)<1);settings.Close();
            foreground=PassiveWindow.GetForegroundWindow();await c.SimulateAsync("Qual é a diferença entre mitose e meiose?");for(int attempt=0;attempt<50&&c.History.Count==0;attempt++)await Task.Delay(100);await Yield();
            Check("Synthetic pipeline renders an answer",c.History.Count==1&&c.Popover.IsVisible);Check("Automatic response does not take foreground focus",PassiveWindow.GetForegroundWindow()==foreground);Capture(c.Popover,Path.Combine(dir,"answer.png"));
            var turns=Enumerable.Range(0,20).Select(n=>new Turn("Pergunta de estudo "+n,new string('A',120)+" "+c.History[0].Answer,DateTimeOffset.UtcNow)).ToArray();
            c.Popover.Open(c.Orb,turns,25);await Yield();Check("Dense history has vertical scroll",c.Popover.Scroller.ScrollableHeight>0);Capture(c.Popover,Path.Combine(dir,"history-dense.png"));
            var copied=new List<string>();var cleared=0;
            var compact=new PopoverWindow(()=>cleared++,copied.Add);
            var shortTurns=new[]{new Turn("O que é mitose?","A mitose forma duas células com o mesmo número de cromossomos.",DateTimeOffset.UtcNow),new Turn("E a meiose?","A meiose forma quatro células com metade desse número.",DateTimeOffset.UtcNow)};
            compact.Open(c.Orb,shortTurns,25);await Yield();Capture(compact,Path.Combine(dir,"history-two-turns.png"));
            Check("Two short responses fit without unnecessary scroll",compact.Scroller.ScrollableHeight<1&&compact.Height<=320);
            var buttons=Descendants(compact).OfType<Button>().ToArray();var copyButtons=buttons.Where(b=>AutomationProperties.GetName(b)=="Copiar resposta").ToArray();
            Check("Copy icons are compact and accessible",copyButtons.Length==2&&copyButtons.All(b=>b.ActualWidth<=28&&b.ActualHeight<=28&&b.ToolTip is string));
            copyButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Copy invokes the correct answer without touching real clipboard",copied.SequenceEqual(new[]{shortTurns[1].Answer}));
            buttons.Single(b=>AutomationProperties.GetName(b)=="Limpar memória e histórico").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Compact clear action is reachable",cleared==1);
            buttons.Single(b=>AutomationProperties.GetName(b)=="Fechar histórico").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Compact close action hides popover",!compact.IsVisible);compact.Close();
            c.Orb.SetStatus(new(Phase.Transcribing,"Transcrevendo"));await Yield();Capture(c.Orb,Path.Combine(dir,"orb-transcribing.png"));
            c.Orb.SetStatus(new(Phase.Discarded,"Descartado"));await Yield();Capture(c.Orb,Path.Combine(dir,"orb-discarded.png"));
            c.Orb.SetStatus(new(Phase.Thinking,"Pensando"));await Yield();Capture(c.Orb,Path.Combine(dir,"orb-thinking.png"));
            c.Clear();Check("Clear empties session and closes popover",c.History.Count==0&&!c.Popover.IsVisible);
            await MarkdownSmoke.RunAsync(dir,c.Orb,Check);
            c.Orb.Hide();await VisualSmoke.RunAsync(dir,Check);
        }
        catch(Exception ex) {checks.Add(new{name="Smoke failure",passed=false,error=ex.Message});Environment.ExitCode=1;}
        File.WriteAllText(Path.Combine(dir,"checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task Yield() {await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;} }
    public static RenderTargetBitmap Capture(Window w,string file,double dpi=96)
    {
        w.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth*dpi/96),(int)Math.Ceiling(w.ActualHeight*dpi/96),dpi,dpi,PixelFormats.Pbgra32);bitmap.Render(w);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);encoder.Save(stream);return bitmap;
    }
}
