using System.Windows.Documents;
using System.Windows.Threading;
namespace StudyWhisper.App;

/// <summary>Regression fixtures for actual native scroll geometry, never OS input or a real conversation.</summary>
public static class PopoverSmoke
{
    public static async Task RunAsync(string dir,OrbWindow orb,Action<string,bool> check)
    {
        var foreground=PassiveWindow.GetForegroundWindow();var popover=new PopoverWindow(()=>{},_=>{},_=>{});
        var at=DateTimeOffset.UtcNow;
        var old=Enumerable.Range(0,3).Select(i=>new Turn($"PERGUNTA ANTIGA {i}",string.Join("\n\n",Enumerable.Range(0,7).Select(n=>$"Resposta antiga {i}, parágrafo {n}. "+new string('a',110))),at.AddMinutes(i))).ToList();
        popover.Open(orb,old,25);await Yield();popover.Scroller.ScrollToTop();popover.Scroller.UpdateLayout();await Yield();
        var current=new Turn("PERGUNTA ATUAL",LongAnswer("INÍCIO DA RESPOSTA ATUAL","FIM DA RESPOSTA ATUAL"),at.AddMinutes(4));old.Add(current);
        popover.Open(orb,old,25,true);await Yield();
        check("New long Markdown answer starts visibly instead of previous answer or its own ending",Visible(popover,"INÍCIO DA RESPOSTA ATUAL")&&!Visible(popover,"FIM DA RESPOSTA ATUAL")&&popover.Scroller.VerticalOffset>0);
        UiSmoke.Capture(popover,Path.Combine(dir,"popover-new-long-start.png"));
        popover.Scroller.ScrollToTop();popover.Scroller.UpdateLayout();await Yield();var reading=popover.Scroller.VerticalOffset;
        await Task.Delay(300);await Yield();check("Voluntary history reading is not continuously repositioned",Math.Abs(popover.Scroller.VerticalOffset-reading)<1&&Visible(popover,"PERGUNTA ANTIGA 0"));
        popover.Open(orb,old,25);await Yield();check("Same visible history refresh preserves reading offset without needing hover",Math.Abs(popover.Scroller.VerticalOffset-reading)<1);
        UiSmoke.Capture(popover,Path.Combine(dir,"popover-history-voluntary.png"));
        popover.Hide();popover.Open(orb,old,25);await Yield();check("Reopening hidden history frames latest answer from its beginning",Visible(popover,"INÍCIO DA RESPOSTA ATUAL")&&!Visible(popover,"FIM DA RESPOSTA ATUAL"));
        var shortTurn=new Turn("NOVA PERGUNTA CURTA","RESPOSTA CURTA MAIS RECENTE.",at.AddMinutes(5));old.Add(shortTurn);popover.Open(orb,old,25,true);await Yield();
        check("New short answer starts at top with old history available above",Visible(popover,"RESPOSTA CURTA MAIS RECENTE.")&&!Visible(popover,"FIM DA RESPOSTA ATUAL")&&Top(popover,"NOVA PERGUNTA CURTA")<45);
        UiSmoke.Capture(popover,Path.Combine(dir,"popover-new-short.png"));
        old.Add(new("PENÚLTIMA PERGUNTA",LongAnswer("INÍCIO PENÚLTIMO","FIM PENÚLTIMO"),at.AddMinutes(6)));popover.Open(orb,old,25,true);
        old.Add(new("ÚLTIMA PERGUNTA",LongAnswer("INÍCIO MAIS RECENTE","FIM MAIS RECENTE"),at.AddMinutes(7)));popover.Open(orb,old,25,true);await Yield();
        check("Consecutive replies invalidate obsolete pending scroll and show most recent beginning",Visible(popover,"INÍCIO MAIS RECENTE")&&!Visible(popover,"INÍCIO PENÚLTIMO")&&!Visible(popover,"FIM MAIS RECENTE"));
        UiSmoke.Capture(popover,Path.Combine(dir,"popover-consecutive.png"));
        old.Add(new(new string('p',1100),LongAnswer("INÍCIO APÓS PERGUNTA LONGA","FIM APÓS PERGUNTA LONGA"),at.AddMinutes(8)));popover.Open(orb,old,25,true);await Yield();
        check("A long question cannot consume viewport and hide current answer beginning",Visible(popover,"INÍCIO APÓS PERGUNTA LONGA")&&Top(popover,"INÍCIO APÓS PERGUNTA LONGA")<25);
        // User interaction before the pending callback cancels its re-anchor without injecting desktop input.
        popover.Open(orb,old,25,true);popover.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseWheelEvent});
        popover.Scroller.ScrollToTop();popover.Scroller.UpdateLayout();await Yield();
        check("Interaction invalidates deferred positioning and retains user-selected history",popover.Scroller.VerticalOffset<1);
        popover.Open(orb,old,25,true);popover.Hide();await Yield();popover.Open(orb,old,25);await Yield();
        check("Hide cancels deferred layout and a later reopen still shows newest start",Visible(popover,"INÍCIO APÓS PERGUNTA LONGA"));
        check("New answer anchoring keeps NOACTIVATE and foreground focus",popover.NoActivateVerified&&PassiveWindow.GetForegroundWindow()==foreground);
        popover.Close();
    }
    private static string LongAnswer(string first,string last)=>$"**{first}**\n\n"+string.Join("\n\n",Enumerable.Range(0,9).Select(i=>$"Parágrafo de estudo {i} com *ênfase*, `código` e conteúdo sintético para rolagem. "+new string('x',105)))+$"\n\n{last}";
    private static string Text(TextBlock value)=>new TextRange(value.ContentStart,value.ContentEnd).Text;
    private static TextBlock Find(PopoverWindow p,string marker)=>Descendants(p.Scroller).OfType<TextBlock>().First(t=>Text(t).Contains(marker));
    private static double Top(PopoverWindow p,string marker)=>Find(p,marker).TranslatePoint(new Point(),p.Scroller).Y;
    private static bool Visible(PopoverWindow p,string marker){var t=Find(p,marker);var y=t.TranslatePoint(new Point(),p.Scroller).Y;return y>=-1&&y<p.Scroller.ViewportHeight-1;}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private static async Task Yield(){await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
}
