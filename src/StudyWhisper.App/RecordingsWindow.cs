using System.Windows.Interop;
namespace StudyWhisper.App;

public sealed class RecordingsWindow : Window
{
    private readonly Func<IReadOnlyList<RecordingSnapshot>> load;
    private readonly ComboBox sessions=new(){DisplayMemberPath=nameof(RecordingSnapshot.Label),MinWidth=170,Margin=new Thickness(0,0,0,8),BorderBrush=Ui.Line};
    private readonly TextBox transcript=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Padding=new Thickness(8),FontSize=14};
    private readonly TextBox draft=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(8),FontSize=14,Height=86,BorderBrush=Ui.Line};
    private readonly TextBox original=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(8),FontSize=13,Height=150,BorderBrush=Ui.Line};
    private readonly StackPanel draftPanel=new();
    private readonly TextBlock draftLabel=Ui.Text("Provisório — pode mudar com mais contexto",12,Ui.Muted);
    private readonly Expander originals=new(){Header="Trechos originais preservados",FontSize=12};
    private string selectedTranscript="";
    private readonly TextBlock status=Ui.Text("",13,Ui.Muted);
    private OutsideClickDismissal? outside;
    private bool destroying,refreshing;
    private string? renderedId;
    private OrbWindow? anchor;
    public RecordingsWindow(Func<IReadOnlyList<RecordingSnapshot>> load,Action<string>? copyText=null)
    {
        this.load=load;Title="StudyWhisper · Gravações";Width=380;Height=390;ShowInTaskbar=false;Topmost=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Ui.Style(this);
        System.Windows.Automation.AutomationProperties.SetName(sessions,"Gravações salvas");System.Windows.Automation.AutomationProperties.SetName(transcript,"Texto consolidado");System.Windows.Automation.AutomationProperties.SetName(draft,"Texto provisório ou a revisar");System.Windows.Automation.AutomationProperties.SetName(original,"Trechos originais preservados");
        var layout=new DockPanel{Margin=new Thickness(12)};Content=new Border{BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Child=layout};
        var header=new DockPanel();var close=Ui.IconButton("\uE711","Fechar gravações",Hide);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var copy=Ui.IconButton("\uE8C8","Copiar transcrição",()=>{try{if(selectedTranscript.Length>0){if(copyText is not null)copyText(selectedTranscript);else Clipboard.SetText(selectedTranscript);}}catch(System.Runtime.InteropServices.COMException){status.Text="Área de transferência ocupada. Tente copiar novamente.";}});DockPanel.SetDock(copy,Dock.Right);header.Children.Add(copy);
        header.Children.Add(Ui.Text("Transcrição",14));DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);DockPanel.SetDock(sessions,Dock.Top);layout.Children.Add(sessions);
        var footer=Ui.Text("Transcrição salva neste computador. Áudio não é guardado. Clique fora para fechar.",12,Ui.Muted);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        DockPanel.SetDock(status,Dock.Bottom);layout.Children.Add(status);
        var right=new StackPanel();right.Children.Add(Ui.Text("Consolidado",12,Ui.Muted));transcript.Height=110;right.Children.Add(transcript);
        draftPanel.Children.Add(draftLabel);draftPanel.Children.Add(draft);right.Children.Add(draftPanel);originals.Content=original;right.Children.Add(originals);
        var rightScroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        layout.Children.Add(rightScroll);SizeChanged+=(_,_)=>transcript.Height=Math.Clamp(ActualHeight*.30,65,150);
        sessions.SelectionChanged+=(_,_)=>{if(!refreshing)Render();};
        IsVisibleChanged+=(_,_)=>{outside?.Dispose();outside=null;if(IsVisible)outside=new(()=>new[]{new WindowInteropHelper(this).Handle},Hide);};
        Closing+=(_,e)=>{if(!destroying){e.Cancel=true;Hide();}};
    }
    public void Open(string? selectId=null,OrbWindow? orb=null)
    {
        if(orb is not null&&!ReferenceEquals(anchor,orb)){if(anchor is not null)anchor.LocationChanged-=AnchorChanged;anchor=orb;anchor.LocationChanged+=AnchorChanged;}
        if(anchor is not null)ToolAnchor.Place(this,anchor,390);Show();Refresh(selectId);Activate();
    }
    private void AnchorChanged(object? sender,EventArgs e){if(IsVisible&&anchor is not null)ToolAnchor.Place(this,anchor,390);}
    public void Refresh(string? selectId=null)
    {
        if(!IsVisible&&IsLoaded)return;
        try
        {
            var selected=selectId??(sessions.SelectedItem as RecordingSnapshot)?.Id;var all=load();refreshing=true;
            sessions.ItemsSource=all;sessions.SelectedItem=all.FirstOrDefault(s=>s.Id==selected)??all.FirstOrDefault();refreshing=false;Render();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){refreshing=false;status.Text="Não foi possível ler as gravações. Confira o acesso ao armazenamento local.";}
    }
    private void Render()
    {
        if(sessions.SelectedItem is not RecordingSnapshot selected){status.Text="Nenhuma gravação. Use Gravar no menu do círculo.";transcript.Text="";draft.Text="";original.Text="";selectedTranscript="";draftPanel.Visibility=Visibility.Collapsed;return;}
        selectedTranscript=selected.Transcript;var text=selected.ConsolidatedTranscript;
        var newSelection=renderedId!=selected.Id;renderedId=selected.Id;
        if(transcript.Text!=text)
        {
            var atEnd=transcript.VerticalOffset>=transcript.ExtentHeight-transcript.ViewportHeight-2;var offset=transcript.VerticalOffset;var start=transcript.SelectionStart;var length=transcript.SelectionLength;
            transcript.Text=text;
            if(newSelection){transcript.Select(0,0);transcript.ScrollToHome();}
            else{transcript.Select(Math.Min(start,text.Length),Math.Min(length,Math.Max(0,text.Length-start)));if(atEnd&&length==0)transcript.ScrollToEnd();else transcript.ScrollToVerticalOffset(offset);}
        }
        draft.Text=selected.DraftTranscript;draftPanel.Visibility=draft.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        draftLabel.Text=selected.NeedsReview?"Bordas a revisar — nenhum trecho foi descartado":"Provisório — pode mudar com mais contexto";
        original.Text=selected.OriginalTranscript;originals.Visibility=original.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        status.Text=$"{RecordingSnapshot.StateLabel(selected.State)} · {selected.Model}"+(selected.Notice.Length>0?"\n"+selected.Notice:"");
    }
    public void Destroy(){destroying=true;outside?.Dispose();outside=null;if(anchor is not null)anchor.LocationChanged-=AnchorChanged;Close();}
}
