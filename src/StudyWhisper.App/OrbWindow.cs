using System.Globalization;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
namespace StudyWhisper.App;

public enum LensScene { Paused, Listening, Filtering, FilterApproved, FilterRejected, Transcribing, TranscriptReady, EmptyTranscript, Classifying, JevIgnored, JevWaiting, JevApproved, Generating, ResponseReady, Throttled, Error, Recording, RecordingTransition }

/// <summary>Fixed indicator with optional actions in one passive, rounded panel.</summary>
public sealed class OrbWindow : PassiveWindow
{
    public const double PanelSize=116;
    internal bool HasTools {get;private set;}
    internal double OverlaySize=>HasTools?PanelSize:72;
    internal Rect AvailablePanelSpace(double minimumWidth,double minimumHeight)
    {
        var work=SystemParameters.WorkArea;
        var bounds=new Rect(double.IsFinite(Left)?Left:work.Right-OverlaySize,double.IsFinite(Top)?Top:work.Top,OverlaySize,OverlaySize);
        if(PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var screen=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
            var logical=target.TransformFromDevice;
            work=new Rect(logical.Transform(new Point(screen.Left,screen.Top)),logical.Transform(new Point(screen.Right,screen.Bottom)));
            bounds=new Rect(logical.Transform(PointToScreen(new Point())),logical.Transform(PointToScreen(new Point(ActualWidth,ActualHeight))));
        }
        work.Inflate(-8,-8);
        var below=Math.Clamp(bounds.Bottom+8,work.Top,work.Bottom);
        var leftSpace=Math.Max(0,bounds.Left-8-work.Left);
        if(work.Bottom-below<minimumHeight&&leftSpace>=minimumWidth)
            return new Rect(work.Left,work.Top,leftSpace,work.Height);
        var right=Math.Clamp(bounds.Right,work.Left,work.Right);
        return new Rect(work.Left,below,Math.Max(1,right-work.Left),Math.Max(1,work.Bottom-below));
    }
    internal void AttachTools(CaptureControls tools)
    {
        HasTools=true;Width=Height=PanelSize;
        Content=null;
        var layout=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        drawing.Width=drawing.Height=72;drawing.HorizontalAlignment=HorizontalAlignment.Center;
        layout.Children.Add(drawing);layout.Children.Add(tools);
        var surface=new Border{Background=Brushes.White,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Padding=new Thickness(2),CornerRadius=new CornerRadius(16),Child=layout,ContextMenu=drawing.ContextMenu};
        drawing.ContextMenu=null;Content=surface;anchor.Request();
    }
    public FlowState Flow {get;}=new();
    public AudioBands Bands {get;private set;}
    public bool EffectiveReduceMotion=>reduceMotion||!SystemParameters.ClientAreaAnimation;
    public bool AnimationRunning=>pulse.IsEnabled;
    public long RenderCount=>drawing.RenderCount;
    private CaptureHealth? captureHealth;
    private long processedFrames,processingFaults;
    public bool RemoteLimited=>status.Phase==Phase.Throttled;
    public bool CaptureStreaming=>captureHealth?.Streaming==true;
    public string CaptureSummary=>captureHealth is null?"":!captureHealth.Requested?"Áudio local: pausado":$"Áudio local: {(captureHealth.Streaming?"ouvindo":"sem quadros recentes")} · quadros recebidos {captureHealth.Frames}, processados {processedFrames} · falhas locais {processingFaults} · aberturas {captureHealth.SourceOpens} · último quadro há {captureHealth.LastFrameAgeMs:0} ms";
    public void SetCaptureHealth(CaptureHealth health,long processed,long faults){var before=Scene;captureHealth=health;processedFrames=processed;processingFaults=faults;ChangedScene(before);UpdateHelp();drawing.InvalidateVisual();}
    public LensScene Scene
    {
        get
        {
            if(recordingLabel.Length>0)return recordingLive?LensScene.Recording:LensScene.RecordingTransition;
            if(status.Phase==Phase.Recovering)return LensScene.Throttled;
            if(status.Phase==Phase.Throttled)return CaptureStreaming?LensScene.Listening:LensScene.Throttled;
            if(status.Phase==Phase.Blocked)return LensScene.Error;
            if(captureHealth is {Requested:true,Streaming:false})return LensScene.Throttled;
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
    private readonly MonitorAnchor anchor;
    private readonly OrbClick click=new();
    private OutsideClickDismissal? menuDismissal;
    private readonly DispatcherTimer pulse=new(){Interval=TimeSpan.FromMilliseconds(1000.0/15)};
    private readonly DispatcherTimer settle=new(){Interval=TimeSpan.FromSeconds(3)};
    private bool reduceMotion;
    private DateTimeOffset bandAt;
    private long transitionAt;
    private Status status=new(Phase.Paused,"Monitoramento pausado");
    private string recordingLabel="";
    private bool recordingLive;
    public string RecordingLabel=>recordingLabel;
    public void SetRecordingState(string label,bool live=false){recordingLabel=label;recordingLive=live;UpdateHelp();drawing.InvalidateVisual();}
    public Status CurrentStatus=>status;
    internal double Motion=>EffectiveReduceMotion?0:Environment.TickCount64/1000.0;
    internal double SceneOpacity=>EffectiveReduceMotion||!Flow.Enabled?1:.4+.6*Math.Clamp(Stopwatch.GetElapsedTime(transitionAt).TotalMilliseconds/150,0,1);
    private bool Transitioning=>Stopwatch.GetElapsedTime(transitionAt).TotalMilliseconds<150;
    public OrbWindow(Action togglePanel,Action clear,Action exit,Action? openPanel=null)
    {
        Title="StudyWhisper · Monitoramento";Width=Height=72;AllowsTransparency=true;Background=Brushes.Transparent;
        drawing=new FlowDrawing(this){Cursor=Cursors.Hand};Content=drawing;
        AutomationProperties.SetName(drawing,"Abrir ou fechar painel do StudyWhisper");
        anchor=new MonitorAnchor(this,size:()=>OverlaySize);
        drawing.MouseLeftButtonDown+=(_,e)=>{click.Begin(e.GetPosition(drawing));drawing.CaptureMouse();e.Handled=true;};
        drawing.MouseMove+=(_,e)=>click.Move(e.GetPosition(drawing));
        drawing.LostMouseCapture+=(_,_)=>click.Cancel();
        drawing.MouseLeftButtonUp+=(_,e)=>{var p=e.GetPosition(drawing);var invoke=click.End(p,p.X>=0&&p.Y>=0&&p.X<=drawing.ActualWidth&&p.Y<=drawing.ActualHeight);drawing.ReleaseMouseCapture();e.Handled=true;if(invoke)togglePanel();};
        var menu=new ContextMenu{StaysOpen=false};
        menu.Opened+=(_,_)=>{menuDismissal?.Dispose();menuDismissal=new(()=>new[]{(PresentationSource.FromVisual(menu) as System.Windows.Interop.HwndSource)?.Handle??IntPtr.Zero},()=>menu.IsOpen=false);};
        menu.Closed+=(_,_)=>{menuDismissal?.Dispose();menuDismissal=null;};
        foreach(var (label,action) in new[]{("Abrir painel",openPanel??togglePanel),("Limpar memória",clear),("Sair",exit)})
        {var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}drawing.ContextMenu=menu;
        pulse.Tick+=(_,_)=>{if(DateTimeOffset.UtcNow-bandAt>TimeSpan.FromMilliseconds(350))Bands=AudioBands.Silent;drawing.InvalidateVisual();RefreshTimer();};
        settle.Tick+=(_,_)=>{settle.Stop();if(!Flow.RemoteBusy&&Flow.Filter!=StepMark.Active){var before=Scene;Flow.Collapse();ChangedScene(before);drawing.InvalidateVisual();RefreshTimer();}};
        SystemParameters.StaticPropertyChanged+=SystemChanged;UpdateHelp();
    }
    private void SystemChanged(object? s,System.ComponentModel.PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(SystemParameters.WorkArea))anchor.Request();if(e.PropertyName==nameof(SystemParameters.ClientAreaAnimation)){drawing.InvalidateVisual();RefreshTimer();}}
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
    public void EndRecovery()
    {
        if(status.Phase!=Phase.Recovering)return;
        if(!Flow.RemoteBusy)Flow.Collapse();
        SetStatus(new(Phase.Listening,"Entrada de áudio reconectada"));
    }
    private void UpdateHelp()
    {
        var message=(status.Phase is Phase.Error or Phase.Throttled or Phase.Recovering or Phase.Blocked?status.Message:Flow.Reason)+(Flow.Queued>0?$" · Fila {Flow.Queued}/2":"")+(Flow.Incoming.Length>0?" · "+Flow.Incoming:"");
        if(recordingLabel.Length>0)message=recordingLabel;
        else if(RemoteLimited&&CaptureStreaming)message="Ouvindo normalmente. O provedor limitou temporariamente os envios. "+message;
        if(CaptureSummary.Length>0)message+="\n"+CaptureSummary;
        drawing.ToolTip=message+"\nClique para abrir ou fechar o painel. Clique com o botão direito para ver as ações.";
        AutomationProperties.SetHelpText(drawing,message);AutomationProperties.SetHelpText(this,message);
    }
    protected override void OnClosed(EventArgs e){anchor.Dispose();menuDismissal?.Dispose();pulse.Stop();settle.Stop();SystemParameters.StaticPropertyChanged-=SystemChanged;base.OnClosed(e);}
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
    {if(!icon)size=11;var key=(text,size,color,icon);if(!texts.TryGetValue(key,out var t)){t=new(text,CultureInfo.GetCultureInfo("pt-BR"),FlowDirection.LeftToRight,new Typeface(icon?"Segoe MDL2 Assets":"Segoe UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);texts[key]=t;}return t;}
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi){texts.Clear();base.OnDpiChanged(oldDpi,newDpi);}
    private void Center(DrawingContext d,string text,double x,double y,double size,Brush color,bool icon=false){var t=Text(text,size,color,icon);d.DrawText(t,new(x-t.Width/2,y-t.Height/2));}
    protected override void OnRender(DrawingContext d)
    {
        RenderCount++;base.OnRender(d);d.PushClip(OuterClip);
        if(!owner.HasTools)d.DrawEllipse(Glass,new Pen(Ui.Line,1),new(36,36),31,31);
        d.DrawEllipse(null,new Pen(Ui.Line,.65),new(36,36),24,24);
        if(owner.Scene==LensScene.Recording){d.DrawEllipse(Red,null,new(56,18),4,4);}
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
                LensScene.Recording=>("\uE720","Ao vivo",Red),LensScene.RecordingTransition=>("\uE823","",Amber),
                LensScene.Throttled=>("\uE823","",Amber),LensScene.Paused=>("\uE769","Parado",Ui.Muted),LensScene.Listening=>("\uE720",owner.RemoteLimited?"Provedor":"Escuta",Ui.Ink),
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
