using System.Windows.Interop;
using System.Windows.Shapes;
namespace StudyWhisper.App;

public static class ToolAnchor
{
    public static void Place(Window panel,OrbWindow orb,double height,double width=380)
    {
        var work=SystemParameters.WorkArea;panel.Width=Math.Max(180,Math.Min(width,work.Width-16));
        var top=Math.Min(orb.Top+orb.Height+32,work.Bottom-100);var available=Math.Max(90,work.Bottom-top-8);
        panel.Height=Math.Min(height,available);panel.Left=Math.Clamp(orb.Left+orb.Width-panel.Width,work.Left+8,Math.Max(work.Left+8,work.Right-panel.Width-8));panel.Top=Math.Clamp(top,work.Top+8,Math.Max(work.Top+8,work.Bottom-panel.Height-8));
    }
}

public sealed class OrbToolsWindow : PassiveWindow
{
    private readonly OrbWindow orb;
    public Button MonitorButton {get;}
    public Button RecordButton {get;}
    public Button RewriteButton {get;}
    public OrbToolsWindow(OrbWindow orb,Action toggle,Action record,Action rewrite)
    {
        this.orb=orb;Title="StudyWhisper · Ações";Width=96;Height=26;AllowsTransparency=true;Background=Brushes.Transparent;
        var panel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};Content=new Border{Background=Brushes.White,BorderBrush=Ui.Line,BorderThickness=new Thickness(.6),CornerRadius=new CornerRadius(13),Child=panel};
        MonitorButton=Ui.IconButton("\uE768","Iniciar monitoramento",toggle);MonitorButton.Width=30;panel.Children.Add(MonitorButton);
        RecordButton=Ui.IconButton("","Gravar",record);RecordButton.Width=30;panel.Children.Add(RecordButton);
        RewriteButton=Ui.IconButton("\uE70F","Rewrite",rewrite);RewriteButton.Width=30;panel.Children.Add(RewriteButton);SetState("Gravar");
        orb.IsVisibleChanged+=VisibleChanged;orb.LocationChanged+=Moved;orb.SizeChanged+=Resized;Position();
    }
    private void Position(){Left=orb.Left+(orb.Width-Width)/2;Top=orb.Top+orb.Height+2;}
    private void Moved(object? s,EventArgs e)=>Position();
    private void Resized(object s,SizeChangedEventArgs e)=>Position();
    private void VisibleChanged(object s,DependencyPropertyChangedEventArgs e){if(orb.IsVisible){Position();Show();}else Hide();}
    public void SetMonitoringState(bool enabled,bool available)
    {
        var label=enabled?"Pausar monitoramento":"Iniciar monitoramento";MonitorButton.Content=Ui.Icon(enabled?"\uE769":"\uE768",14);MonitorButton.ToolTip=label;MonitorButton.IsEnabled=available;System.Windows.Automation.AutomationProperties.SetName(MonitorButton,label);
    }
    public void SetState(string action)
    {
        RecordButton.Content=action=="Parar"?new Rectangle{Width=9,Height=9,Fill=Brushes.Firebrick}:new Ellipse{Width=9,Height=9,Fill=Brushes.Firebrick};
        RecordButton.ToolTip=action;RecordButton.IsEnabled=action is "Gravar" or "Parar";System.Windows.Automation.AutomationProperties.SetName(RecordButton,action);
    }
    protected override void OnClosed(EventArgs e){orb.IsVisibleChanged-=VisibleChanged;orb.LocationChanged-=Moved;orb.SizeChanged-=Resized;base.OnClosed(e);}
}

public sealed class RewriteWindow : Window
{
    private readonly OrbWindow orb;
    private readonly Func<string,CancellationToken,Task<string>> run;
    public TextBox Input {get;}=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=105,Padding=new Thickness(7),BorderBrush=Ui.Line};
    public TextBox Result {get;}=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=125,Padding=new Thickness(7),BorderBrush=Ui.Line};
    public Button Submit {get;}
    private readonly TextBlock status=Ui.Text("Revisão · Padrão · Remover elementos típicos de IA",12,Ui.Muted);
    private CancellationTokenSource? active;
    private long generation;
    private OutsideClickDismissal? outside;
    private bool destroying;
    public RewriteWindow(OrbWindow orb,Func<string,CancellationToken,Task<string>> run)
    {
        this.orb=orb;this.run=run;Title="StudyWhisper · Rewrite";Topmost=true;ShowInTaskbar=false;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Ui.Style(this);
        System.Windows.Automation.AutomationProperties.SetName(Input,"Texto a revisar");System.Windows.Automation.AutomationProperties.SetName(Result,"Revisão");
        var content=new StackPanel{Margin=new Thickness(12)};Content=new Border{BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Background=Brushes.White,Child=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        var header=new DockPanel();var close=Ui.IconButton("\uE711","Fechar revisão",Hide);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);header.Children.Add(Ui.Text("Rewrite",14));content.Children.Add(header);content.Children.Add(status);content.Children.Add(Ui.Text("Texto a revisar",12,Ui.Muted));content.Children.Add(Input);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Submit=Ui.Button("Reescrever",()=>_=SubmitAsync());actions.Children.Add(Submit);actions.Children.Add(Ui.IconButton("\uE8C8","Copiar revisão",()=>{try{if(Result.Text.Length>0)Clipboard.SetText(Result.Text);}catch(System.Runtime.InteropServices.COMException){status.Text="Área de transferência ocupada.";}}));content.Children.Add(actions);content.Children.Add(Ui.Text("Revisão",12,Ui.Muted));content.Children.Add(Result);
        IsVisibleChanged+=(_,_)=>{outside?.Dispose();outside=null;if(IsVisible)outside=new(()=>new[]{new WindowInteropHelper(this).Handle,new WindowInteropHelper(orb).Handle},Hide);else Cancel();};
        Closing+=(_,e)=>{if(!destroying){e.Cancel=true;Hide();}};orb.LocationChanged+=AnchorChanged;orb.SizeChanged+=AnchorResized;
    }
    private void AnchorChanged(object? s,EventArgs e){if(IsVisible)ToolAnchor.Place(this,orb,390);}
    private void AnchorResized(object s,SizeChangedEventArgs e)=>AnchorChanged(s,e);
    public void Open(){ToolAnchor.Place(this,orb,390);Show();Activate();Input.Focus();}
    private void Cancel(){generation++;active?.Cancel();Input.IsReadOnly=false;Submit.IsEnabled=active is null;Submit.Content=active is null?"Reescrever":"Cancelando…";}
    public async Task SubmitAsync()
    {
        if(active is not null){Cancel();status.Text="Revisão cancelada. Texto preservado.";return;}
        if(string.IsNullOrWhiteSpace(Input.Text))return;
        if(Input.Text.Length>12000){status.Text="O texto excede 12.000 caracteres e foi preservado. Divida-o antes de enviar.";return;}
        var cancellation=new CancellationTokenSource();active=cancellation;var version=++generation;var text=Input.Text;Input.IsReadOnly=true;Submit.Content="Cancelar";status.Text="Reescrevendo…";
        try{var result=await run(text,cancellation.Token);if(version==generation&&!cancellation.IsCancellationRequested){Result.Text=result;status.Text="Revisão concluída · instrução canônica do LMDocs";}}
        catch(OperationCanceledException){if(version==generation)status.Text="Revisão cancelada. Texto preservado.";}
        catch(Exception ex){if(version==generation)status.Text=ex is ApiException or ArgumentException or InvalidDataException?ex.Message:"Não foi possível revisar. O texto foi preservado.";}
        finally{if(ReferenceEquals(active,cancellation)){active=null;Input.IsReadOnly=false;Submit.IsEnabled=true;Submit.Content="Reescrever";}cancellation.Dispose();}
    }
    public void Destroy(){destroying=true;Cancel();outside?.Dispose();orb.LocationChanged-=AnchorChanged;orb.SizeChanged-=AnchorResized;Close();}
}
