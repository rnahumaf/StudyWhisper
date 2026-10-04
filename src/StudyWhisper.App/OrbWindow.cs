using System.Globalization;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
namespace StudyWhisper.App;

public enum LensScene { Paused, Listening, Filtering, FilterApproved, FilterRejected, Transcribing, TranscriptReady, EmptyTranscript, Classifying, JevIgnored, JevWaiting, JevApproved, Generating, ResponseReady, Throttled, Error }

/// <summary>One fixed 72 DIP circle. Real stages replace its internal scene; never expand the window.</summary>
public sealed class OrbWindow : PassiveWindow
{
    public FlowState Flow {get;}=new();
    public AudioBands Bands {get;private set;}
    public bool EffectiveReduceMotion=>reduceMotion||!SystemParameters.ClientAreaAnimation;
    public bool AnimationRunning=>pulse.IsEnabled;
    public long RenderCount=>drawing.RenderCount;
    public LensScene Scene
    {
        get
        {
            if(status.Phase==Phase.Throttled)return LensScene.Throttled;
            if(!Flow.Enabled)return status.Phase==Phase.Error?LensScene.Error:LensScene.Paused;
            if(!Flow.Expanded)return LensScene.Listening;
            if(Flow.Cloud==StepMark.Error||Flow.Jev==StepMark.Error||Flow.Whisper==StepMark.Error)return LensScene.Error;
            if(Flow.Cloud==StepMark.Approved)return LensScene.ResponseReady;
            if(Flow.Cloud==StepMark.Active)return LensScene.Generating;
            if(Flow.Jev==StepMark.Approved)return LensScene.JevApproved;
            if(Flow.Jev==StepMark.Rejected)return LensScene.JevIgnored;
            if(Flow.Jev==StepMark.Waiting)return LensScene.JevWaiting;
            if(Flow.Jev==StepMark.Active)return LensScene.Classifying;
            if(Flow.Whisper==StepMark.Approved)return LensScene.TranscriptReady;
            if(Flow.Whisper==StepMark.Rejected)return LensScene.EmptyTranscript;
            if(Flow.Whisper==StepMark.Active)return LensScene.Transcribing;
            return Flow.Filter switch {StepMark.Active=>LensScene.Filtering,StepMark.Approved=>LensScene.FilterApproved,StepMark.Rejected=>LensScene.FilterRejected,_=>LensScene.Listening};
        }
    }
    private readonly FlowDrawing drawing;
    private readonly DispatcherTimer pulse=new(){Interval=TimeSpan.FromMilliseconds(1000.0/15)};
    private readonly DispatcherTimer settle=new(){Interval=TimeSpan.FromSeconds(3)};
    private bool reduceMotion;
    private DateTimeOffset bandAt;
    private long transitionAt;
    private Status status=new(Phase.Paused,"Monitoramento pausado");
    internal double Motion=>EffectiveReduceMotion?0:Environment.TickCount64/1000.0;
    internal double SceneOpacity=>EffectiveReduceMotion||!Flow.Enabled?1:.4+.6*Math.Clamp(Stopwatch.GetElapsedTime(transitionAt).TotalMilliseconds/150,0,1);
    private bool Transitioning=>Stopwatch.GetElapsedTime(transitionAt).TotalMilliseconds<150;
    public OrbWindow(Action history,Action settings,Action toggle,Action clear,Action exit)
    {
        Title="StudyWhisper · Monitoramento";Width=Height=72;AllowsTransparency=true;Background=Brushes.Transparent;
        drawing=new FlowDrawing(this){Cursor=Cursors.Hand};Content=drawing;
        AutomationProperties.SetName(drawing,"StudyWhisper: abrir histórico da sessão");
        drawing.MouseLeftButtonUp+=(_,_)=>history();
        var menu=new ContextMenu();
        foreach(var (label,action) in new[]{("Histórico da sessão",history),("Configurações",settings),("Ativar / desativar monitoramento",toggle),("Limpar memória e histórico",clear),("Sair",exit)})
        {var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}drawing.ContextMenu=menu;
        pulse.Tick+=(_,_)=>{if(DateTimeOffset.UtcNow-bandAt>TimeSpan.FromMilliseconds(350))Bands=AudioBands.Silent;drawing.InvalidateVisual();RefreshTimer();};
        settle.Tick+=(_,_)=>{settle.Stop();if(!Flow.RemoteBusy&&Flow.Filter!=StepMark.Active){var before=Scene;Flow.Collapse();ChangedScene(before);drawing.InvalidateVisual();RefreshTimer();}};
        SystemParameters.StaticPropertyChanged+=SystemChanged;Anchor();UpdateHelp();
    }
    private void SystemChanged(object? s,System.ComponentModel.PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(SystemParameters.WorkArea))Anchor();if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)){drawing.InvalidateVisual();RefreshTimer();}}
    private void Anchor(){var area=SystemParameters.WorkArea;Left=area.Right-Width-20;Top=area.Top+20;}
    private void ChangedScene(LensScene previous){if(previous!=Scene)transitionAt=Stopwatch.GetTimestamp();}
    private void RefreshTimer(){if(!EffectiveReduceMotion&&(Flow.RemoteBusy||Flow.Expanded&&Flow.Filter==StepMark.Active||Bands.Intensity>0||Flow.Enabled&&Transitioning))pulse.Start();else pulse.Stop();}
    public void Configure(bool reduced){reduceMotion=reduced;RefreshTimer();drawing.InvalidateVisual();}
    public void Spectrum(AudioBands bands){
            if(!Flow.Enabled)return;Bands=bands;bandAt=DateTimeOffset.UtcNow;RefreshTimer();if(EffectiveReduceMotion)drawing.InvalidateVisual();}
    public void Apply(FlowEvent signal)
    {
        var before=Scene;if(!Flow.Apply(signal))return;
        if(signal.Kind is FlowKind.Cancelled or FlowKind.Enabled)Bands=AudioBands.Silent;
        settle.Stop();if(Flow.Expanded&&!Flow.RemoteBusy&&Flow.Filter!=StepMark.Active)settle.Start();
        ChangedScene(before);UpdateHelp();drawing.InvalidateVisual();RefreshTimer();
    }
    // Actionable errors remain in the tooltip. Status never fabricates a stage result.
    public void SetStatus(Status value){var before=Scene;status=value;ChangedScene(before);UpdateHelp();drawing.InvalidateVisual();RefreshTimer();}
    public void EndThrottle()
    {
        if(status.Phase!=Phase.Throttled)return;
        if(!Flow.RemoteBusy&&Flow.Filter!=StepMark.Active)Flow.Collapse();
        SetStatus(new(Phase.Listening,"Monitoramento ativado"));
    }
    private void UpdateHelp()
    {
        var message=(status.Phase is Phase.Error or Phase.Throttled?status.Message:Flow.Reason)+(Flow.Queued>0?$" · Fila {Flow.Queued}/2":"")+(Flow.Incoming.Length>0?" · "+Flow.Incoming:"");
        drawing.ToolTip=message+"\nGraves (ciano): 80–350 Hz · médios (violeta): 350–2000 Hz · agudos (âmbar): 2–8 kHz.\nCores representam energia sonora, não intenção. Glifos STT são decorativos.\nClique: histórico · botão direito: monitoramento e configurações.";
        AutomationProperties.SetHelpText(drawing,message);AutomationProperties.SetHelpText(this,message);
    }
    protected override void OnClosed(EventArgs e){pulse.Stop();settle.Stop();SystemParameters.StaticPropertyChanged-=SystemChanged;base.OnClosed(e);}
}

internal sealed class FlowDrawing : FrameworkElement
{
    private readonly OrbWindow owner;
    private static Brush Color(byte r,byte g,byte b){var x=new SolidColorBrush(System.Windows.Media.Color.FromRgb(r,g,b));x.Freeze();return x;}
    private static readonly Brush Cyan=Color(19,126,160),Violet=Color(115,80,186),Amber=Color(173,112,20),Green=Color(40,123,91),Red=Color(167,56,72);
    private static readonly Brush Glass=GlassBrush();
    private static Brush GlassBrush(){var b=new LinearGradientBrush(Colors.White,System.Windows.Media.Color.FromRgb(234,244,251),new Point(0,0),new Point(1,1));b.Freeze();return b;}
    private static readonly EllipseGeometry OuterClip=Circle(31.5),InnerClip=Circle(25);
    private static EllipseGeometry Circle(double radius){var c=new EllipseGeometry(new Point(36,36),radius,radius);c.Freeze();return c;}
    private readonly Dictionary<(string,double,Brush,bool),FormattedText> texts=new();
    public long RenderCount {get;private set;}
    public FlowDrawing(OrbWindow owner){this.owner=owner;UseLayoutRounding=true;SnapsToDevicePixels=true;}
    private FormattedText Text(string text,double size,Brush color,bool icon=false)
    {var key=(text,size,color,icon);if(!texts.TryGetValue(key,out var t)){t=new(text,CultureInfo.GetCultureInfo("pt-BR"),FlowDirection.LeftToRight,new Typeface(icon?"Segoe MDL2 Assets":"Segoe UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);texts[key]=t;}return t;}
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi){texts.Clear();base.OnDpiChanged(oldDpi,newDpi);}
    private void Center(DrawingContext d,string text,double x,double y,double size,Brush color,bool icon=false){var t=Text(text,size,color,icon);d.DrawText(t,new(x-t.Width/2,y-t.Height/2));}
    protected override void OnRender(DrawingContext d)
    {
        RenderCount++;base.OnRender(d);d.PushClip(OuterClip);
        d.DrawEllipse(Glass,new Pen(Ui.Line,1),new(36,36),31,31);
        d.DrawEllipse(null,new Pen(Ui.Line,.65),new(36,36),24,24);
        var bands=owner.Bands;var values=new[]{bands.Low,bands.Mid,bands.High};var colors=new[]{Cyan,Violet,Amber};
        for(var band=0;band<3;band++)for(var bar=0;bar<8;bar++)
        {
            var angle=(-150+band*120+bar*12)*Math.PI/180;var length=1.5+values[band]*3.5;
            d.PushOpacity(.2+values[band]*.8);d.DrawLine(new Pen(colors[band],1.7),new(36+Math.Cos(angle)*26,36+Math.Sin(angle)*26),new(36+Math.Cos(angle)*(26+length),36+Math.Sin(angle)*(26+length)));d.Pop();
        }
        d.PushClip(InnerClip);d.PushOpacity(owner.SceneOpacity);var scene=owner.Scene;
        if(scene==LensScene.Filtering)Filter(d);
        else if(scene==LensScene.Transcribing)Transcription(d);
        else if(scene==LensScene.Classifying)Judgment(d,"\uE9F5",Cyan);
        else if(scene==LensScene.JevWaiting)Judgment(d,"\uE823",Amber);
        else if(scene==LensScene.JevIgnored)Judgment(d,"\uE711",Red);
        else if(scene==LensScene.JevApproved)Judgment(d,"\uE73E",Green);
        else if(scene==LensScene.Generating)Generation(d);
        else
        {
            var (glyph,tag,color)=scene switch
            {
                LensScene.Throttled=>("\uE823","",Amber),LensScene.Paused=>("\uE769","",Ui.Ink),LensScene.Listening=>("\uE720","",Ui.Ink),
                LensScene.FilterApproved=>("\uE73E","Filtro",Green),LensScene.FilterRejected=>("\uE711","Filtro",Red),
                LensScene.TranscriptReady=>("\uE73E","STT",Green),LensScene.EmptyTranscript=>("\uE711","STT",Red),
                LensScene.ResponseReady=>("\uE73E","Texto",Green),_=>("\uE7BA","",Red)
            };
            Center(d,glyph,36,tag.Length>0?31:36,24,color,true);if(tag.Length>0)Center(d,tag,36,50,10.5,Ui.Muted);
        }
        d.Pop();
        if(owner.Flow.Queued>0){d.DrawEllipse(Brushes.White,new Pen(Ui.Line,.7),new(49,23),6,6);Center(d,owner.Flow.Queued.ToString(),49,23,9.5,Ui.Ink);}
        d.Pop();d.Pop();
    }
    private void Filter(DrawingContext d)
    {
        for(var band=0;band<3;band++)
        {
            var value=band==0?owner.Bands.Low:band==1?owner.Bands.Mid:owner.Bands.High;var y=27+band*5;
            d.PushOpacity(.3+value*.7);d.DrawLine(new Pen(band==0?Cyan:band==1?Violet:Amber,1+value){DashStyle=DashStyles.Dot},new(12,y),new(30,y));d.Pop();
        }
        d.DrawLine(new Pen(Cyan,1.8),new(33,23),new(33,40));var tilt=owner.EffectiveReduceMotion?0:Math.Sin(owner.Motion*2.5)*3;
        for(var y=25;y<=38;y+=6)d.DrawLine(new Pen(Cyan,1.2),new(33,y),new(39,y+tilt));
        Center(d,"\uE71C",47,32,15,Cyan,true);Center(d,"Filtro",36,50,10.5,Ui.Muted);
        if(!owner.EffectiveReduceMotion){var x=13+(owner.Motion*.8%1)*16;d.DrawEllipse(Violet,null,new(x,32),1.5,1.5);}
    }
    private void Transcription(DrawingContext d)
    {
        Center(d,"\uE70F",36,28,22,Cyan,true);var drift=owner.EffectiveReduceMotion?0:Math.Sin(owner.Motion*2)*1.5;Center(d,"Aa · _",36+drift,49,11,Violet);
        if(owner.EffectiveReduceMotion||owner.Motion%1<.6)d.DrawLine(new Pen(Cyan,1),new(52,44),new(52,52));
    }
    private void Judgment(DrawingContext d,string glyph,Brush color)
    {
        var diamond=new StreamGeometry();using(var c=diamond.Open()){c.BeginFigure(new(36,15),false,true);c.LineTo(new(51,30),true,false);c.LineTo(new(36,45),true,false);c.LineTo(new(21,30),true,false);}diamond.Freeze();
        d.DrawGeometry(null,new Pen(Ui.Line,.9),diamond);Center(d,glyph,36,30,19,color,true);Center(d,"JEV",36,51,10.5,Ui.Muted);
        if(glyph=="\uE9F5"&&!owner.EffectiveReduceMotion){var angle=owner.Motion*2;d.DrawEllipse(Violet,null,new(36+Math.Cos(angle)*13,30+Math.Sin(angle)*13),1.5,1.5);}
    }
    private void Generation(DrawingContext d)
    {
        Center(d,"\uE753",36,30,23,Cyan,true);Center(d,"Texto",36,50,10.5,Ui.Muted);
        if(!owner.EffectiveReduceMotion)for(var i=0;i<3;i++){var angle=owner.Motion*1.7+i*2*Math.PI/3;d.DrawEllipse(Violet,null,new(36+Math.Cos(angle)*18,30+Math.Sin(angle)*18),1.5,1.5);}
    }
}
