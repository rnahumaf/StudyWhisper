using System.Windows.Automation;

namespace StudyWhisper.App;

internal sealed class TranscriptsView : DockPanel
{
    private readonly Func<IReadOnlyList<RecordingSnapshot>> load;
    private readonly ComboBox sessions=new(){DisplayMemberPath=nameof(RecordingSnapshot.Label),MinHeight=32,VerticalContentAlignment=VerticalAlignment.Center};
    private readonly TextBox text=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,FontSize=13,Padding=new Thickness(2,6,4,4),BorderThickness=new Thickness(0)};
    private readonly TextBlock notice=Ui.Text("",12,Ui.Muted);
    private readonly Button copy;
    private IReadOnlyList<RecordingSnapshot> snapshots=Array.Empty<RecordingSnapshot>();
    private string? renderedId;
    private bool refreshing;

    public TranscriptsView(Func<IReadOnlyList<RecordingSnapshot>> load)
    {
        this.load=load;
        AutomationProperties.SetName(sessions,"Transcrições salvas");
        AutomationProperties.SetName(text,"Transcrição");
        TextBlock.SetLineHeight(text,20);
        var toolbar=new DockPanel{Margin=new Thickness(0,2,0,6)};
        copy=PanelIcons.Button("copy","Copiar transcrição",Copy);
        SetDock(copy,Dock.Right);toolbar.Children.Add(copy);
        toolbar.Children.Add(sessions);
        SetDock(toolbar,Dock.Top);Children.Add(toolbar);
        notice.Margin=new Thickness(1,3,1,6);
        SetDock(notice,Dock.Top);Children.Add(notice);Children.Add(text);
        sessions.SelectionChanged+=(_,_)=>{if(!refreshing)Render();};
    }

    public void Refresh(string? selectId=null)
    {
        var previous=snapshots;
        try{snapshots=load();}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("Não foi possível ler as transcrições. Confira o acesso ao armazenamento local.");
            return;
        }
        var id=selectId??(sessions.SelectedItem as RecordingSnapshot)?.Id;
        refreshing=true;
        try
        {
            if(sessions.ItemsSource is null||!previous.Select(s=>(s.Id,s.State)).SequenceEqual(snapshots.Select(s=>(s.Id,s.State))))
                sessions.ItemsSource=snapshots;
            var index=snapshots.ToList().FindIndex(s=>s.Id==id);
            sessions.SelectedIndex=index>=0?index:snapshots.Count>0?0:-1;
        }
        finally{refreshing=false;}
        Render();
    }

    private void Render()
    {
        var index=sessions.SelectedIndex;
        var selected=index>=0&&index<snapshots.Count?snapshots[index]:null;
        var value=selected is null?"":string.Join(Environment.NewLine,selected.Segments
            .Where(s=>!s.Superseded&&!s.Pending)
            .Select(s=>s.Note.Length>0?"[Trecho indisponível]":s.Text).Where(s=>s.Length>0));
        var changed=renderedId!=selected?.Id;
        if(changed||text.Text!=value)
        {
            var offset=text.VerticalOffset;
            var atEnd=offset>=text.ExtentHeight-text.ViewportHeight-2;
            var start=Math.Min(text.SelectionStart,value.Length);
            var length=Math.Min(text.SelectionLength,value.Length-start);
            text.Text=value;
            if(changed){text.Select(0,0);text.ScrollToHome();}
            else
            {
                text.Select(start,length);
                if(selected?.State is RecordingState.Recording or RecordingState.Stopping&&atEnd&&length==0)text.ScrollToEnd();
                else text.ScrollToVerticalOffset(offset);
            }
        }
        renderedId=selected?.Id;
        sessions.IsEnabled=snapshots.Count>0;
        copy.IsEnabled=value.Length>0;
        var status=selected switch
        {
            null=>"Nenhuma transcrição salva. Escolha Transcrição e clique em Iniciar.",
            {Notice.Length:>0}=>selected.Notice,
            {State:RecordingState.Stopping}=>"Concluindo os últimos trechos…",
            {State:RecordingState.Recording} when value.Length==0=>"Aguardando o primeiro trecho de fala…",
            {State:RecordingState.Interrupted}=>"Transcrição interrompida. Os trechos disponíveis estão abaixo.",
            _ when value.Length==0=>"Esta sessão terminou sem texto transcrito.",
            _=>""
        };
        ShowNotice(status);
    }
    private void Copy()
    {
        try{Clipboard.SetText(text.Text);}
        catch(System.Runtime.InteropServices.COMException){ShowNotice("Área de transferência ocupada. Tente copiar novamente.");}
    }
    private void ShowNotice(string message)
    {
        notice.Text=message;notice.Visibility=message.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
}
