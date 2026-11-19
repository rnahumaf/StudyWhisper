using System.Windows.Automation;

namespace StudyWhisper.App;

internal sealed class CaptureControls : Grid
{
    private readonly Controller controller;
    private readonly ComboBox mode;
    private readonly Button startStop;
    private readonly bool compact;
    public bool IsChoosingMode=>mode.IsDropDownOpen;

    public CaptureControls(Controller controller,bool compact)
    {
        this.controller=controller;
        this.compact=compact;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        mode=new ComboBox
        {
            ItemsSource=new[]{"Perguntas","Transcrição"},MinHeight=compact?28:32,
            VerticalContentAlignment=VerticalAlignment.Center,FontSize=compact?11:13,
            Margin=new Thickness(0,0,compact?2:8,0),Focusable=!compact
        };
        AutomationProperties.SetName(mode,"Modo de captura");
        ToolTipService.SetShowOnDisabled(mode,true);
        mode.DropDownOpened+=(_,_)=>controller.Popover.Hide();
        if(compact)
        {
            var passiveItem=new Style(typeof(ComboBoxItem));
            passiveItem.Setters.Add(new Setter(FocusableProperty,false));
            mode.ItemContainerStyle=passiveItem;
        }
        Children.Add(mode);
        startStop=PanelIcons.Button("play","Iniciar",controller.ToggleCapture);
        startStop.Width=compact?28:94;
        startStop.MinHeight=compact?28:32;
        startStop.Height=compact?28:32;
        startStop.Margin=new Thickness(0);
        startStop.Focusable=!compact;
        if(!compact)
        {
            startStop.Background=new SolidColorBrush(Color.FromRgb(231,241,245));
            startStop.FontSize=13;
        }
        SetColumn(startStop,1);
        Children.Add(startStop);
        mode.SelectionChanged+=(_,_)=>
        {
            if(mode.SelectedIndex>=0)
                controller.SelectMode(mode.SelectedIndex==0?StudyMode.Questions:StudyMode.Transcription);
        };
        Refresh();
    }

    public void Refresh()
    {
        var questions=controller.SelectedMode==StudyMode.Questions;
        mode.SelectedIndex=questions?0:1;
        mode.IsEnabled=controller.CanSelectMode;
        mode.ToolTip=mode.IsEnabled?"Escolher modo":"Pare a captura para trocar de modo.";
        var verb=controller.Capturing?"Parar":"Iniciar";
        var label=verb+(questions?" modo Perguntas":" transcrição");
        PanelIcons.Set(startStop,controller.Capturing?"square":"play",label,compact?null:verb);
        startStop.IsEnabled=controller.ModeReady;
    }
}
