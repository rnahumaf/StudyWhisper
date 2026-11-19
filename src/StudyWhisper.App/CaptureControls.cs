using System.Windows.Input;

namespace StudyWhisper.App;

internal sealed class CaptureControls : Grid
{
    private static readonly Brush ActionBackground = new SolidColorBrush(Color.FromRgb(231, 241, 245));
    private static readonly ControlTemplate ModeTemplate = CreateModeTemplate();

    private readonly Controller controller;
    private readonly Grid modeButtons = new();
    private readonly RadioButton questions;
    private readonly RadioButton transcription;
    private readonly Button startStop;
    private readonly bool compact;

    public CaptureControls(Controller controller, bool compact)
    {
        this.controller = controller;
        this.compact = compact;
        var height = compact ? 28 : 32;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        modeButtons.Margin = new Thickness(0, 0, compact ? 6 : 8, 0);
        modeButtons.ColumnDefinitions.Add(new ColumnDefinition());
        modeButtons.ColumnDefinitions.Add(new ColumnDefinition());
        questions = CreateModeButton(StudyMode.Questions, "messages-square", "Perguntas", height);
        questions.Margin = new Thickness(0, 0, 2, 0);
        transcription = CreateModeButton(StudyMode.Transcription, "file-text", "Transcrição", height);
        SetColumn(transcription, 1);
        modeButtons.Children.Add(questions);
        modeButtons.Children.Add(transcription);
        Children.Add(modeButtons);

        startStop = PanelIcons.Button("play", "Iniciar", controller.ToggleCapture);
        startStop.Width = compact ? 28 : 94;
        startStop.MinHeight = startStop.Height = height;
        startStop.Margin = new Thickness(0);
        startStop.Focusable = !compact;
        startStop.Background = ActionBackground;
        startStop.FontSize = 13;
        SetColumn(startStop, 1);
        Children.Add(startStop);
        Refresh();
    }

    private RadioButton CreateModeButton(StudyMode mode, string icon, string label, double height)
    {
        var button = new RadioButton
        {
            Height = height,
            FontSize = 13,
            Focusable = !compact,
            Cursor = Cursors.Hand,
            Template = ModeTemplate
        };
        PanelIcons.Set(button, icon, label, compact ? null : label);
        ToolTipService.SetShowOnDisabled(button, true);
        button.Checked += (_, _) => controller.SelectMode(mode);
        return button;
    }

    public void Refresh()
    {
        var questionsSelected = controller.SelectedMode == StudyMode.Questions;
        (questionsSelected ? questions : transcription).IsChecked = true;
        modeButtons.IsEnabled = controller.CanSelectMode;
        questions.ToolTip = modeButtons.IsEnabled ? "Perguntas" : "Perguntas · Troca disponível após encerrar a captura.";
        transcription.ToolTip = modeButtons.IsEnabled ? "Transcrição" : "Transcrição · Troca disponível após encerrar a captura.";
        var verb = controller.Capturing ? "Parar" : "Iniciar";
        var label = verb + (questionsSelected ? " modo Perguntas" : " transcrição");
        PanelIcons.Set(startStop, controller.Capturing ? "square" : "play", label, compact ? null : verb);
        startStop.IsEnabled = controller.ModeReady;
    }

    private static ControlTemplate CreateModeTemplate()
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        surface.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(content);

        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(237, 243, 246)), "Surface"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, ActionBackground, "Surface"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Accent, "Surface"));
        template.Triggers.Add(selected);
        var inactive = new MultiTrigger();
        inactive.Conditions.Add(new Condition(IsEnabledProperty, false));
        inactive.Conditions.Add(new Condition(RadioButton.IsCheckedProperty, false));
        inactive.Setters.Add(new Setter(OpacityProperty, 0.45, "Surface"));
        template.Triggers.Add(inactive);
        return template;
    }
}
