using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Threading;
namespace StudyWhisper.App;

public static class MarkdownSmoke
{
    public static async Task RunAsync(string dir,OrbWindow orb,Action<string,bool> check)
    {
        var foreground=PassiveWindow.GetForegroundWindow();var copied=new List<string>();var opened=new List<Uri>();
        var popover=new PopoverWindow(()=>{},copied.Add,opened.Add);
        const string markdown="""
            **Resposta de estudo** com *ênfase* e `código` em texto nativo.

            Primeiro parágrafo completo. A explicação mantém as condições que mudam a interpretação.

            - Conceito principal com **termo importante**.
            - Uma fonte [oficial](https://example.org/reference), aberta somente por clique.
            - Um link [inseguro](javascript:alert(1)) fica sem ação.

            1. Compare os conceitos.
            2. Confira a condição relevante.

            ```cs
            var resultado = 2 + 2;
            Console.WriteLine(resultado);
            ```

            <script>alert('conteúdo literal, sem execução')</script>

            Mais um parágrafo que precisa de rolagem e continua disponível no histórico da sessão.
            """;
        var turn=new Turn("Pode responder em lista, com exemplos e código?",markdown,DateTimeOffset.UtcNow){Sources=new[]{new Citation("Fonte oficial", "https://example.org/reference")},SearchRequests=1};
        popover.Open(orb,new[]{turn},25);await Yield();
        check("Markdown response grows within ten-line body limit and scrolls after it",popover.Height>=290&&popover.Height<=PopoverWindow.BodyLimit+58&&popover.Scroller.ScrollableHeight>0);
        check("Native Markdown and source row do not take foreground focus",popover.NoActivateVerified&&PassiveWindow.GetForegroundWindow()==foreground&&opened.Count==0);
        var values=Descendants(popover).OfType<TextBlock>().ToArray();var inlines=values.SelectMany(t=>AllInlines(t.Inlines)).ToArray();
        check("Markdown renders bold, italic, inline code and fenced code in WPF",inlines.OfType<Bold>().Any()&&inlines.OfType<Italic>().Any()&&inlines.OfType<Run>().Any(r=>r.FontFamily.Source=="Consolas")&&values.Any(t=>t.FontFamily.Source=="Consolas"));
        var links=inlines.OfType<Hyperlink>().ToArray();check("Only safe explicit Markdown and source links are active",links.Length==2&&links.All(l=>l.ToolTip is string));
        links[0].RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));check("Link activation uses injected action once, no real browser",opened.Count==1&&opened[0].Host=="example.org");
        var copy=Descendants(popover).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Copiar resposta");copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("Copy is plain readable text with paragraph/list/code structure",copied.Count==1&&copied[0].Contains("Resposta de estudo com ênfase")&&copied[0].Contains("• Conceito")&&copied[0].Contains("var resultado")&&!copied[0].Contains("**")&&!copied[0].Contains("```"));
        foreach(var dpi in new[]{96,144,192})UiSmoke.Capture(popover,Path.Combine(dir,$"answer-markdown-{dpi}dpi.png"),dpi);
        popover.Scroller.ScrollToBottom();await Yield();UiSmoke.Capture(popover,Path.Combine(dir,"answer-markdown-bottom.png"));
        check("Long response scroll reaches code and literal HTML",popover.Scroller.VerticalOffset>0&&Math.Abs(popover.Scroller.VerticalOffset-popover.Scroller.ScrollableHeight)<1);
        popover.Open(orb,new[]{new Turn("Uma pergunta curta?","Uma resposta curta e direta.",DateTimeOffset.UtcNow)},25);await Yield();
        check("Short response shrinks the popover without unnecessary scrolling",popover.Height<170&&popover.Scroller.ScrollableHeight<1);UiSmoke.Capture(popover,Path.Combine(dir,"answer-short.png"));
        var top=orb.Top;orb.Top=SystemParameters.WorkArea.Bottom-90;popover.Open(orb,new[]{turn},25);await Yield();
        check("Popover remains inside available work area near bottom",popover.Top>=SystemParameters.WorkArea.Top&&popover.Top+popover.Height<=SystemParameters.WorkArea.Bottom);orb.Top=top;
        orb.SetStatus(new(Phase.Throttled,"Limite local por minuto. Aguarde 37 s."));await Yield();UiSmoke.Capture(orb,Path.Combine(dir,"orb-throttled.png"));
        check("Throttle uses wait glyph distinct from processing error",orb.Scene==LensScene.Throttled&&AutomationProperties.GetHelpText(orb).Contains("37 s"));
        orb.EndThrottle();check("Cooldown ending clears wait glyph without network or replay",orb.Scene!=LensScene.Throttled);
        orb.SetStatus(new(Phase.Paused,"Pausado"));popover.Close();
    }
    private static IEnumerable<System.Windows.Documents.Inline> AllInlines(InlineCollection values)
    {foreach(var inline in values){yield return inline;if(inline is Span span)foreach(var child in AllInlines(span.Inlines))yield return child;}}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private static async Task Yield()=>await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
}
