using System.Windows.Automation;
using System.Windows.Input;

namespace StudyWhisper.App;

public sealed record SessionTranscript(string Id,DateTimeOffset At,string Text,string State,bool Active=false)
{
    public string Label=>$"{At.LocalDateTime:T} · {State}";
}

public sealed class DashboardWindow : Window
{
    private readonly Controller controller;
    private readonly CaptureControls controls;
    private readonly TextBlock activity=Ui.Text("",14,Ui.Muted);
    private readonly TabControl pages=new(){BorderThickness=new Thickness(0),Padding=new Thickness(0,8,0,0)};
    private readonly TabControl questions=new(){BorderThickness=new Thickness(0),Padding=new Thickness(0,6,0,0)};
    private readonly StackPanel conversation=new();
    private readonly ComboBox speech=new(){DisplayMemberPath=nameof(SessionTranscript.Label),MinHeight=32,VerticalContentAlignment=VerticalAlignment.Center};
    private readonly TextBox heard=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,FontSize=13,BorderThickness=new Thickness(0),Padding=new Thickness(2,6,4,4)};
    private readonly TextBlock emptySpeech=Ui.Text("Nenhuma fala transcrita nesta sessão.",13,Ui.Muted);
    private readonly TextBlock copyNotice=Ui.Text("",12,Ui.Muted);
    private readonly Button copySpeech;
    private readonly TranscriptsView transcripts;
    private readonly SettingsWindow settings;
    private IReadOnlyList<Turn> previousTurns=Array.Empty<Turn>();
    private IReadOnlyList<SessionTranscript> previousSpeech=Array.Empty<SessionTranscript>();
    private bool destroying;
    public bool DisplaysContent=>IsVisible&&WindowState!=WindowState.Minimized;

    public DashboardWindow(Controller controller)
    {
        this.controller=controller;
        Title="StudyWhisper · Painel";Width=420;Height=720;MinWidth=340;MinHeight=480;MaxWidth=520;
        WindowStartupLocation=WindowStartupLocation.Manual;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;ShowInTaskbar=true;Topmost=true;Ui.Style(this);
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(4),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(8)});
        PanelIcons.StyleNavigation(this);
        var shell=new DockPanel{Margin=new Thickness(12,5,12,10)};
        Content=new Border{BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Background=Brushes.White,Child=shell};

        var header=new DockPanel();
        var close=PanelIcons.Button("x","Fechar painel",Hide);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        activity.VerticalAlignment=VerticalAlignment.Center;activity.Margin=new Thickness(0);activity.FontSize=13;
        AutomationProperties.SetLiveSetting(activity,AutomationLiveSetting.Polite);header.Children.Add(activity);
        DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
        if(controller.HistoryNotice.Length>0)
        {
            var storageNotice=Ui.Text(controller.HistoryNotice,13);DockPanel.SetDock(storageNotice,Dock.Top);shell.Children.Add(storageNotice);
        }
        controls=new CaptureControls(controller,compact:false){Margin=new Thickness(0,3,0,8)};
        DockPanel.SetDock(controls,Dock.Top);shell.Children.Add(controls);
        copyNotice.Visibility=Visibility.Collapsed;DockPanel.SetDock(copyNotice,Dock.Bottom);shell.Children.Add(copyNotice);

        var answers=new ScrollViewer{Content=conversation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        questions.Items.Add(new TabItem{Header="Respostas",Content=answers});
        var heardPage=new DockPanel();var heardToolbar=new DockPanel();
        copySpeech=PanelIcons.Button("copy","Copiar fala",()=>Copy(heard.Text));DockPanel.SetDock(copySpeech,Dock.Right);heardToolbar.Children.Add(copySpeech);heardToolbar.Children.Add(speech);
        DockPanel.SetDock(heardToolbar,Dock.Top);heardPage.Children.Add(heardToolbar);
        DockPanel.SetDock(emptySpeech,Dock.Top);heardPage.Children.Add(emptySpeech);heardPage.Children.Add(heard);
        AutomationProperties.SetName(speech,"Falas desta sessão");AutomationProperties.SetName(heard,"Fala transcrita no modo Perguntas");
        speech.SelectionChanged+=(_,_)=>RenderSpeech();
        questions.Items.Add(new TabItem{Header="Falas ouvidas",Content=heardPage});

        transcripts=new TranscriptsView(()=>controller.Recordings);
        settings=new SettingsWindow(controller,embedded:true);
        var settingsContent=(FrameworkElement)settings.Content;settings.Content=null;
        pages.Items.Add(new TabItem{Header="Perguntas",Content=questions});
        pages.Items.Add(new TabItem{Header="Transcrições",Content=transcripts});
        pages.Items.Add(new TabItem{Header="Configurações",Content=settingsContent});
        AutomationProperties.SetName(pages,"Navegação do painel");shell.Children.Add(pages);
        pages.SelectionChanged+=(_,e)=>{if(e.Source==pages){copyNotice.Visibility=Visibility.Collapsed;RefreshData();}};
        IsVisibleChanged+=(_,_)=>{if(DisplaysContent){controller.Popover.Hide();RefreshData();}};
        StateChanged+=(_,_)=>{if(DisplaysContent)controller.Popover.Hide();};
        Closing+=(_,e)=>{if(!destroying){e.Cancel=true;Hide();}};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!e.Handled){Hide();e.Handled=true;}};
        controller.Orb.LocationChanged+=AnchorChanged;controller.Orb.SizeChanged+=AnchorResized;
    }

    public void Open(string page)
    {
        ShowModePage(page,controller.ActiveRecordingId);
        Place();Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
        RefreshData();
    }

    public void ShowModePage(string page,string? recordingId=null)
    {
        pages.SelectedItem=pages.Items.Cast<TabItem>().First(item=>Equals(item.Header,page));
        if(page=="Transcrições")transcripts.Refresh(recordingId);
        if(page=="Perguntas")questions.SelectedIndex=0;
    }

    private void Place()
    {
        var space=controller.Orb.AvailablePanelSpace(340,480);
        MaxWidth=Math.Min(520,space.Width);MinWidth=Math.Min(340,MaxWidth);Width=Math.Clamp(Width,MinWidth,MaxWidth);
        MaxHeight=space.Height;MinHeight=Math.Min(480,MaxHeight);Height=Math.Clamp(Height,MinHeight,MaxHeight);
        Left=space.Right-Width;Top=space.Top;
    }
    private void AnchorChanged(object? sender,EventArgs e){if(IsVisible)Place();}
    private void AnchorResized(object sender,SizeChangedEventArgs e)=>AnchorChanged(sender,e);
    public void RefreshControls()
    {
        controls.Refresh();activity.Text=controller.CurrentActivity;
        settings.RefreshActions();
    }

    public void RefreshData()
    {
        if(!IsVisible)return;
        RefreshControls();
        if(pages.SelectedIndex==1){transcripts.Refresh();return;}
        if(pages.SelectedIndex!=0)return;
        var turns=controller.History;
        if(conversation.Children.Count==0||!previousTurns.SequenceEqual(turns))
        {
            previousTurns=turns.ToArray();conversation.Children.Clear();
            if(turns.Count==0)conversation.Children.Add(Ui.Text("As respostas às perguntas de estudo aparecem aqui.",13,Ui.Muted));
            foreach(var turn in turns.Reverse())
            {
                var row=new StackPanel{Margin=new Thickness(0,4,0,14)};
                var tools=new DockPanel();
                var copy=PanelIcons.Button("copy","Copiar resposta",()=>Copy(MarkdownContent.PlainText(turn.Answer)));
                DockPanel.SetDock(copy,Dock.Right);tools.Children.Add(copy);
                var question=Ui.Text(turn.Question,15);question.FontWeight=FontWeights.SemiBold;question.VerticalAlignment=VerticalAlignment.Center;
                question.Margin=new Thickness(0);tools.Children.Add(question);row.Children.Add(tools);
                row.Children.Add(MarkdownView.Render(turn.Answer));
                if(turn.Sources.Count>0)
                {
                    var sources=Ui.Text("Fontes  ",12,Ui.Muted);
                    foreach(var source in turn.Sources.Take(3))
                    {
                        if(!SafeLinks.TryParse(source.Url,out var uri))continue;
                        var link=MarkdownView.Link(uri.Host,source.Url,MarkdownView.Open);
                        link.ToolTip=source.Title+"\n"+uri.AbsoluteUri;sources.Inlines.Add(link);
                        sources.Inlines.Add(new System.Windows.Documents.Run("  "));
                    }
                    row.Children.Add(sources);
                }
                conversation.Children.Add(row);
            }
        }
        var items=controller.SessionTranscripts;
        if(speech.ItemsSource is null||!previousSpeech.SequenceEqual(items))
        {
            previousSpeech=items;
            var id=(speech.SelectedItem as SessionTranscript)?.Id;
            speech.ItemsSource=items;speech.SelectedItem=items.FirstOrDefault(t=>t.Id==id)??items.FirstOrDefault();
            RenderSpeech();
        }
    }

    private void RenderSpeech()
    {
        heard.Text=(speech.SelectedItem as SessionTranscript)?.Text??"";
        speech.IsEnabled=speech.Items.Count>0;copySpeech.IsEnabled=heard.Text.Length>0;
        emptySpeech.Visibility=speech.Items.Count==0?Visibility.Visible:Visibility.Collapsed;
    }

    private void Copy(string value)
    {
        try{Clipboard.SetText(value);copyNotice.Visibility=Visibility.Collapsed;}
        catch(System.Runtime.InteropServices.COMException){copyNotice.Text="Área de transferência ocupada. Tente copiar novamente.";copyNotice.Visibility=Visibility.Visible;}
    }

    public void Destroy()
    {
        destroying=true;
        controller.Orb.LocationChanged-=AnchorChanged;controller.Orb.SizeChanged-=AnchorResized;
        settings.Close();Close();
    }
}
