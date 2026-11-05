using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace StudyWhisper.App;

public sealed class RecordingsWindow : Window
{
    private const double DefaultHeight=540;
    private readonly Func<IReadOnlyList<RecordingSnapshot>> load;
    private readonly Func<string,Task>? rewrite;
    private readonly Action<string>? cancelRewrite;
    private readonly Action<string>? copyText;
    private readonly ComboBox sessions=new(){DisplayMemberPath=nameof(RecordingSnapshot.Label),MinWidth=170,Margin=new Thickness(0,6,0,8),BorderBrush=Ui.Line};
    private readonly TextBox transcript=TextArea(15);
    private readonly TextBox original=TextArea(14);
    private readonly TextBlock transcriptLabel=Ui.Text("Transcrição ao vivo",12,Ui.Muted);
    private readonly TextBlock status=Ui.Text("",12,Ui.Muted);
    private readonly Expander originals=new(){IsExpanded=true,FontSize=12};
    private readonly RowDefinition originalRow=new(){Height=new GridLength(2,GridUnitType.Star)};
    private readonly GridSplitter divider=new(){Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,Background=Ui.Line,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext};
    private readonly Button copy,expand,retry,cancel;
    private string selectedTranscript="",selectedOriginal="";
    private OutsideClickDismissal? outside;
    private bool destroying,refreshing,expanded,retrying;
    private double preferredHeight=DefaultHeight,restoreHeight=DefaultHeight,dragHeight;
    private string? renderedId,retryingId;
    private GridLength originalHeight=new(2,GridUnitType.Star);
    private bool originalOpen=true;
    private RecordingRewriteState renderedRewrite;
    private OrbWindow? anchor;

    private static TextBox TextArea(double fontSize)
    {
        var box=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,BorderThickness=new Thickness(0),Padding=new Thickness(8),FontSize=fontSize};
        TextBlock.SetLineHeight(box,fontSize*1.5);return box;
    }

    public RecordingsWindow(Func<IReadOnlyList<RecordingSnapshot>> load,Action<string>? copyText=null,Func<string,Task>? rewrite=null,Action<string>? cancelRewrite=null)
    {
        this.load=load;this.copyText=copyText;this.rewrite=rewrite;this.cancelRewrite=cancelRewrite;
        Title="StudyWhisper · Gravações";Width=440;Height=DefaultHeight;ShowInTaskbar=false;Topmost=true;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Ui.Style(this);
        System.Windows.Automation.AutomationProperties.SetName(sessions,"Gravações salvas");
        System.Windows.Automation.AutomationProperties.SetName(transcript,"Transcrição ao vivo");
        System.Windows.Automation.AutomationProperties.SetName(original,"Transcrição original sem reescrita");
        var layout=new DockPanel{Margin=new Thickness(12,10,12,0)};
        Content=new Border{BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Child=layout};
        var header=new DockPanel();
        var close=Ui.IconButton("\uE711","Fechar gravações",Hide);close.Focusable=true;DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        expand=Ui.IconButton("\uE70D","Expandir janela para baixo",ToggleSize);expand.Focusable=true;DockPanel.SetDock(expand,Dock.Right);header.Children.Add(expand);
        copy=Ui.IconButton("\uE8C8","Copiar transcrição ao vivo",()=>Copy(selectedTranscript));copy.Focusable=true;DockPanel.SetDock(copy,Dock.Right);header.Children.Add(copy);
        var title=Ui.Text("Transcrição",14);title.Margin=new Thickness(0);title.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(title);
        DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);DockPanel.SetDock(sessions,Dock.Top);layout.Children.Add(sessions);

        var resize=new Thumb{Height=18,Cursor=Cursors.SizeNS,Focusable=true,ToolTip="Arraste para baixo para ampliar. Use ↑ ou ↓ quando selecionado."};
        System.Windows.Automation.AutomationProperties.SetName(resize,"Ajustar altura da transcrição");
        var grip=new FrameworkElementFactory(typeof(Border));grip.SetValue(Border.BackgroundProperty,Ui.Line);grip.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));grip.SetValue(FrameworkElement.WidthProperty,38.0);grip.SetValue(FrameworkElement.HeightProperty,3.0);grip.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);grip.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        var surface=new FrameworkElementFactory(typeof(Border));surface.SetValue(Border.BackgroundProperty,Brushes.Transparent);surface.AppendChild(grip);resize.Template=new ControlTemplate(typeof(Thumb)){VisualTree=surface};
        resize.DragStarted+=(_,_)=>dragHeight=ActualHeight;
        resize.DragDelta+=(_,e)=>{dragHeight+=e.VerticalChange;SetHeight(dragHeight);};
        resize.KeyDown+=(_,e)=>{if(e.Key is Key.Up or Key.Down){SetHeight(ActualHeight+(e.Key==Key.Down?40:-40));e.Handled=true;}};
        DockPanel.SetDock(resize,Dock.Bottom);layout.Children.Add(resize);
        var footer=Ui.Text("Texto salvo neste computador. Áudio não é guardado.",11,Ui.Muted);footer.Margin=new Thickness(0,5,0,0);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};retry=Ui.Button("Reescrever",()=>_=RetryAsync());retry.Margin=new Thickness(0,3,0,0);retry.Visibility=Visibility.Collapsed;actions.Children.Add(retry);
        cancel=Ui.Button("Cancelar reescrita",()=>{if(sessions.SelectedItem is RecordingSnapshot selected)cancelRewrite?.Invoke(selected.Id);});cancel.Visibility=Visibility.Collapsed;actions.Children.Add(cancel);DockPanel.SetDock(actions,Dock.Bottom);layout.Children.Add(actions);
        var statusScroll=new ScrollViewer{Content=status,MaxHeight=72,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,4)};
        DockPanel.SetDock(statusScroll,Dock.Top);layout.Children.Add(statusScroll);

        var body=new Grid();body.RowDefinitions.Add(new(){Height=new GridLength(3,GridUnitType.Star)});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(originalRow);
        var finalPanel=new DockPanel();DockPanel.SetDock(transcriptLabel,Dock.Top);finalPanel.Children.Add(transcriptLabel);finalPanel.Children.Add(transcript);body.Children.Add(finalPanel);
        Grid.SetRow(divider,1);body.Children.Add(divider);
        var originalHeader=new DockPanel{LastChildFill=true};
        var copyOriginal=Ui.IconButton("\uE8C8","Copiar transcrição original",()=>Copy(selectedOriginal));copyOriginal.Focusable=true;DockPanel.SetDock(copyOriginal,Dock.Right);originalHeader.Children.Add(copyOriginal);
        var originalTitle=Ui.Text("Original · sem reescrita",12,Ui.Muted);originalTitle.Margin=new Thickness(0);originalTitle.VerticalAlignment=VerticalAlignment.Center;originalHeader.Children.Add(originalTitle);
        originals.Header=originalHeader;originals.Content=original;originals.HorizontalContentAlignment=HorizontalAlignment.Stretch;originals.VerticalContentAlignment=VerticalAlignment.Stretch;
        originals.Expanded+=(_,_)=>UpdateOriginalLayout();originals.Collapsed+=(_,_)=>UpdateOriginalLayout();Grid.SetRow(originals,2);body.Children.Add(originals);layout.Children.Add(body);
        sessions.SelectionChanged+=(_,_)=>{if(!refreshing)Render();};
        IsVisibleChanged+=(_,_)=>{outside?.Dispose();outside=null;if(IsVisible)outside=new(()=>new[]{new WindowInteropHelper(this).Handle,anchor is null?IntPtr.Zero:new WindowInteropHelper(anchor).Handle},Hide);};
        Closing+=(_,e)=>{if(!destroying){e.Cancel=true;Hide();}};
    }

    private void Copy(string text)
    {
        try{if(text.Length>0){if(copyText is not null)copyText(text);else Clipboard.SetText(text);}}
        catch(System.Runtime.InteropServices.COMException){status.Text="Área de transferência ocupada. Tente copiar novamente.";}
    }
    private void UpdateOriginalLayout()
    {
        var open=originals.Visibility==Visibility.Visible&&originals.IsExpanded;
        if(open!=originalOpen)
        {
            if(originalOpen)originalHeight=originalRow.Height;
            originalRow.Height=open?originalHeight:GridLength.Auto;originalOpen=open;
        }
        divider.Visibility=open?Visibility.Visible:Visibility.Collapsed;
    }
    private double MaximumHeight=>Math.Max(180,SystemParameters.WorkArea.Bottom-Top-8);
    private void SetHeight(double height)
    {
        expanded=false;preferredHeight=Math.Clamp(height,Math.Min(360,MaximumHeight),MaximumHeight);Height=preferredHeight;UpdateExpandLabel();
    }
    private void ToggleSize()
    {
        if(expanded){preferredHeight=restoreHeight;expanded=false;}
        else{restoreHeight=ActualHeight;preferredHeight=MaximumHeight;expanded=true;}
        if(anchor is not null)ToolAnchor.Place(this,anchor,preferredHeight,440);else Height=Math.Min(preferredHeight,MaximumHeight);UpdateExpandLabel();
    }
    private void UpdateExpandLabel()
    {
        var label=expanded?"Restaurar altura da janela":"Expandir janela para baixo";
        expand.Content=Ui.Icon(expanded?"\uE70E":"\uE70D",14);expand.ToolTip=label;System.Windows.Automation.AutomationProperties.SetName(expand,label);
    }
    public void Open(string? selectId=null,OrbWindow? orb=null)
    {
        if(orb is not null&&!ReferenceEquals(anchor,orb)){if(anchor is not null)anchor.LocationChanged-=AnchorChanged;anchor=orb;anchor.LocationChanged+=AnchorChanged;}
        if(anchor is not null)ToolAnchor.Place(this,anchor,preferredHeight,440);Show();Refresh(selectId);Activate();
    }
    private void AnchorChanged(object? sender,EventArgs e){if(IsVisible&&anchor is not null)ToolAnchor.Place(this,anchor,expanded?double.MaxValue:preferredHeight,440);}
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
    private static void SetText(TextBox box,string text,bool reset,bool follow)
    {
        if(box.Text==text&&!reset)return;
        var atEnd=box.VerticalOffset>=box.ExtentHeight-box.ViewportHeight-2;var offset=box.VerticalOffset;var start=box.SelectionStart;var length=box.SelectionLength;
        box.Text=text;
        if(reset){box.Select(0,0);box.ScrollToHome();}
        else{box.Select(Math.Min(start,text.Length),Math.Min(length,Math.Max(0,text.Length-start)));if(follow&&atEnd&&length==0)box.ScrollToEnd();else box.ScrollToVerticalOffset(offset);}
    }
    private void Render()
    {
        if(sessions.SelectedItem is not RecordingSnapshot selected)
        {
            status.Text="Nenhuma gravação. Use Gravar no menu do círculo.";transcript.Text="";original.Text="";selectedTranscript=selectedOriginal="";originals.Visibility=Visibility.Collapsed;retry.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;copy.IsEnabled=false;UpdateOriginalLayout();return;
        }
        var newSelection=renderedId!=selected.Id;var newFinal=selected.RewriteState==RecordingRewriteState.Completed&&renderedRewrite!=RecordingRewriteState.Completed;
        renderedId=selected.Id;renderedRewrite=selected.RewriteState;
        var hasFinal=selected.FinalText.Length>0;selectedTranscript=hasFinal?selected.FinalText:selected.Transcript;selectedOriginal=selected.OriginalTranscript;
        SetText(transcript,selectedTranscript,newSelection||newFinal,!hasFinal);SetText(original,selectedOriginal,newSelection,true);
        transcriptLabel.Text=hasFinal?"Texto final reescrito":selected.State is RecordingState.Recording or RecordingState.Stopping?"Transcrição ao vivo · reescrita ao parar":"Transcrição preservada · sem reescrita final";
        System.Windows.Automation.AutomationProperties.SetName(transcript,hasFinal?"Texto final reescrito":"Transcrição ao vivo");
        var copyLabel=hasFinal?"Copiar texto final":"Copiar transcrição sem reescrita";copy.ToolTip=copyLabel;System.Windows.Automation.AutomationProperties.SetName(copy,copyLabel);copy.IsEnabled=selectedTranscript.Length>0;
        originals.Visibility=selectedOriginal.Length>0?Visibility.Visible:Visibility.Collapsed;UpdateOriginalLayout();
        var rewriteStatus=selected.RewriteState==RecordingRewriteState.NotStarted?"":selected.RewriteLabel;
        status.Text=string.Join("\n",new[]{RecordingSnapshot.StateLabel(selected.State)+(rewriteStatus.Length>0?" · "+rewriteStatus:""),selected.Notice,selected.RewriteNotice}.Where(t=>t.Length>0));
        status.ToolTip="Transcrição: "+selected.Model;
        retry.Visibility=rewrite is not null&&(selected.State is RecordingState.Completed or RecordingState.Interrupted)&&selectedTranscript.Length>0?Visibility.Visible:Visibility.Collapsed;
        var processing=selected.RewriteState==RecordingRewriteState.Processing||retryingId==selected.Id;
        if(processing)retry.Visibility=Visibility.Collapsed;
        cancel.Visibility=cancelRewrite is not null&&processing?Visibility.Visible:Visibility.Collapsed;
        retry.IsEnabled=!retrying;
        retry.Content=hasFinal?"Reescrever novamente":"Reescrever transcrição";
    }
    private async Task RetryAsync()
    {
        if(retrying||rewrite is null||sessions.SelectedItem is not RecordingSnapshot selected)return;
        retrying=true;retryingId=selected.Id;Render();string? failure=null;
        try{await rewrite(selected.Id);}
        catch(OperationCanceledException){failure="Reescrita cancelada. A transcrição foi preservada.";}
        catch(Exception ex) when(ex is ApiException or IOException or UnauthorizedAccessException or InvalidOperationException){failure="Não foi possível reescrever. Conclua a captura atual e confira a chave nas configurações. A transcrição foi preservada.";}
        finally{retrying=false;retryingId=null;Refresh();if(failure is not null)status.Text=failure;}
    }
    public void Destroy(){destroying=true;outside?.Dispose();outside=null;if(anchor is not null)anchor.LocationChanged-=AnchorChanged;Close();}
}
