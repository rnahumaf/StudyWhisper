using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Automation;
namespace StudyWhisper.App;

public static class Ui
{
    public static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(32,43,57));
    public static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(90,103,117));
    public static readonly Brush Accent=new SolidColorBrush(Color.FromRgb(52,105,126));
    public static readonly Brush Line=new SolidColorBrush(Color.FromRgb(218,226,231));
    public static TextBlock Text(string text,double size=14,Brush? color=null)=>new() {Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
    public static Button Button(string label,Action action)
    {
        var b=new Button{Content=label,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,3,8,3),MinHeight=34,Background=Brushes.White,BorderBrush=Line,Foreground=Ink,FontSize=14};
        AutomationProperties.SetName(b,label); b.Click+=(_,_)=>action(); return b;
    }
    public static TextBlock Icon(string glyph,double size=25)=>new() {Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=size,Foreground=Ink,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    public static Button IconButton(string glyph,string label,Action action)
    {
        var b=new Button{Content=Icon(glyph,14),ToolTip=label,Width=26,Height=26,Padding=new Thickness(3),Margin=new Thickness(0),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(1),Cursor=Cursors.Hand,Focusable=false};
        AutomationProperties.SetName(b,label);
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(237,243,246))));template.Triggers.Add(hover);
        b.Template=template;b.Click+=(_,_)=>action();return b;
    }
    public static void Style(Window w) { w.FontFamily=new FontFamily("Segoe UI"); w.FontSize=14; w.Foreground=Ink; w.Background=Brushes.White; }
}

public class PassiveWindow : Window
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetLong(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    public bool NoActivateVerified { get; private set; }
    public PassiveWindow()
    {
        ShowActivated=false; ShowInTaskbar=false; Topmost=true; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; Ui.Style(this);
        SourceInitialized+=(_,_)=>
        {
            var hwnd=new WindowInteropHelper(this).Handle;
            SetLong(hwnd,-20,new IntPtr(GetLong(hwnd,-20).ToInt64()|0x08000000|0x00000080));
            HwndSource.FromHwnd(hwnd).AddHook(Hook);
            NoActivateVerified=(GetLong(hwnd,-20).ToInt64()&0x08000000)!=0;
        };
    }
    private static IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
    { if(msg==0x0021) {handled=true;return new IntPtr(3);} return IntPtr.Zero; }
}

public sealed class PopoverWindow : PassiveWindow
{
    private readonly StackPanel turns=new();
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer timer;
    private readonly PopoverClock clock=new();
    private readonly DockPanel layout;
    private readonly Action<string> copy;
    private readonly Action<Uri> openLink;
    public const double BodyLimit=252;
    private int seconds=25;
    public PopoverWindow(Action clear,Action<string>? copy=null,Action<Uri>? openLink=null)
    {
        this.openLink=openLink??MarkdownView.Open;
        this.copy=copy??(text=>{try{Clipboard.SetText(text);}catch(System.Runtime.InteropServices.COMException){}});
        Title="StudyWhisper • Histórico da sessão";Width=380;Height=320;
        AllowsTransparency=true;Background=Brushes.Transparent;
        layout=new DockPanel{Margin=new Thickness(12)};
        var header=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,5)};
        var close=Ui.IconButton("\uE711","Fechar histórico",Hide);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var clearButton=Ui.IconButton("\uE74D","Limpar memória e histórico",clear);DockPanel.SetDock(clearButton,Dock.Right);header.Children.Add(clearButton);
        var title=Ui.Text("Histórico da sessão",13,Ui.Muted);title.Margin=new Thickness(0);title.VerticalAlignment=VerticalAlignment.Center;title.ToolTip="Fechamento automático pausado enquanto você lê ou interage.";header.Children.Add(title);
        DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=turns};layout.Children.Add(scroll);
        Content=new Border{BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Background=Brushes.White,Child=layout};
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        timer.Tick+=(_,_)=>{if(IsVisible&&clock.Tick(TimeSpan.FromMilliseconds(200),IsMouseOver||IsMouseCaptureWithin)) Hide();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)timer.Start();else timer.Stop();};
        PreviewMouseWheel+=(_,_)=>clock.Reset(seconds);
        PreviewMouseDown+=(_,_)=>clock.Reset(seconds);
        Closed+=(_,_)=>timer.Stop();
    }
    public void Open(OrbWindow orb,IReadOnlyList<Turn> history,int closeSeconds,bool forceBottom=false)
    {
        seconds=closeSeconds;clock.Reset(seconds);
        var keep=IsVisible&&IsMouseOver;var offset=scroll.VerticalOffset;
        turns.Children.Clear();
        if(history.Count==0) turns.Children.Add(Ui.Text("As respostas aparecerão aqui. O monitoramento começa desativado; use o menu da bandeja para configurar e ativar.",14,Ui.Muted));
        for(var index=0;index<history.Count;index++)
        {
            var turn=history[index];
            var metadata=new DockPanel{LastChildFill=true};
            var copyButton=Ui.IconButton("\uE8C8","Copiar resposta",()=>copy(MarkdownContent.PlainText(turn.Answer)));DockPanel.SetDock(copyButton,Dock.Right);metadata.Children.Add(copyButton);
            var time=Ui.Text(turn.At.ToLocalTime().ToString("HH:mm"),11,Ui.Muted);time.Margin=new Thickness(0);time.VerticalAlignment=VerticalAlignment.Center;metadata.Children.Add(time);turns.Children.Add(metadata);
            var question=Ui.Text(turn.Question,12.5,Ui.Muted);question.Margin=new Thickness(0,0,0,3);turns.Children.Add(question);
            turns.Children.Add(MarkdownView.Render(turn.Answer,openLink));
            if(turn.Sources.Count>0)
            {
                var sources=Ui.Text("Fontes: ",11,Ui.Muted);sources.Margin=new Thickness(0,2,0,4);
                foreach(var source in turn.Sources.Take(3))
                {
                    if(!SafeLinks.TryParse(source.Url,out var uri))continue;
                    var link=MarkdownView.Link(uri.Host,source.Url,openLink);link.ToolTip=source.Title+"\n"+uri.AbsoluteUri;
                    sources.Inlines.Add(link);sources.Inlines.Add(new System.Windows.Documents.Run("  "));
                }
                turns.Children.Add(sources);
            }
            if(index<history.Count-1)turns.Children.Add(new Border{Height=1,Background=Ui.Line,Margin=new Thickness(0,7,0,7)});
        }
        var work=SystemParameters.WorkArea;
        Width=Math.Max(160,Math.Min(380,work.Width-16));
        // Ten answer lines (20 DIP each), plus question and small metadata. Longer/history content scrolls.
        var top=Math.Min(orb.Top+orb.Height+8,work.Bottom-96);
        var available=Math.Max(80,work.Bottom-top-8);
        turns.Measure(new Size(Math.Max(1,Width-28),double.PositiveInfinity));
        var body=Math.Min(BodyLimit,turns.DesiredSize.Height);
        Height=Math.Clamp(body+57,80,Math.Min(BodyLimit+57,available));
        Left=Math.Clamp(orb.Left+orb.Width-Width,work.Left+8,Math.Max(work.Left+8,work.Right-Width-8));
        Top=Math.Clamp(top,work.Top+8,Math.Max(work.Top+8,work.Bottom-Height-8));
        Show();scroll.UpdateLayout(); if(keep)scroll.ScrollToVerticalOffset(offset);else if(forceBottom)scroll.ScrollToBottom();
    }
    public ScrollViewer Scroller=>scroll;
}
