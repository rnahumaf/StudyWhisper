using System.Windows.Input;
using System.Windows.Threading;
namespace StudyWhisper.App;

internal static class OverlaySmoke
{
    internal static async Task RunAsync(Controller c,string dir,Action<string,bool> check)
    {
        foreach(var dpi in new uint[]{96,120,144,192,96})
        foreach(var work in new[]{new System.Drawing.Rectangle(0,0,1920,1040),new System.Drawing.Rectangle(-2560,-400,2560,1400),new System.Drawing.Rectangle(1920,120,1280,680)})
        {
            var r=MonitorAnchor.Bounds(work,dpi);
            check($"Physical work area {work.X},{work.Y} DPI {dpi}",r.Width==r.Height&&r.Width==(int)Math.Round(72*dpi/96.0)&&work.Contains(r)&&r.Right==work.Right-(int)Math.Round(20*dpi/96.0));
        }
        check("Primary switch preserves existing monitor",MonitorAnchor.Select("HDMI",new[]{("Laptop",true),("HDMI",false)})=="HDMI");
        check("HDMI removal falls back to primary",MonitorAnchor.Select("HDMI",new[]{("Laptop",true)})=="Laptop");
        check("HDMI reconnect preserves fallback monitor",MonitorAnchor.Select("Laptop",new[]{("Laptop",false),("HDMI",true)})=="Laptop");
        var topology=new[]{new MonitorAnchor.Display("Laptop",true,new(0,0,1920,1040),96),new MonitorAnchor.Display("HDMI",false,new(-2560,-400,2560,1400),144)};
        var placements=new List<System.Drawing.Rectangle>();var simulatedWindow=new Window();
        using(var anchor=new MonitorAnchor(simulatedWindow,()=>topology,placements.Add))
        {
            anchor.Request();await Idle();
            var initial=placements[^1];
            // Call the same notification entry point as the native hook, without sending OS messages.
            anchor.Notify(0x0218);anchor.Notify(0x007E);anchor.Notify(0x001A);anchor.Notify(0x02E0);await Idle();
            check("Simulated resume coalesces power/display/DPI burst without drift",anchor.ApplyCount==2&&placements[^1]==initial&&anchor.Device=="Laptop");
            topology=new[]{topology[0] with{Primary=false},topology[1] with{Primary=true}};
            anchor.Notify(0x007E);await Idle();
            check("Native notification path preserves current monitor after primary switch",anchor.Device=="Laptop"&&placements[^1]==initial);
            topology=new[]{topology[1]};anchor.Notify(0x007E);await Idle();
            check("Native notification path falls back after current display removal",anchor.Device=="HDMI"&&placements[^1]==MonitorAnchor.Bounds(topology[0].Work,144));
            topology=new[]{topology[0] with{Work=new(-1920,100,1920,1000),Dpi=192}};
            anchor.Notify(0x02E0);anchor.Notify(0x0218);await Idle();
            check("Resume after resolution/DPI change uses fresh topology once",anchor.ApplyCount==5&&placements[^1].Width==144&&topology[0].Work.Contains(placements[^1]));
            for(var n=0;n<10;n++){anchor.Notify(0x0218);await Idle();}
            check("Repeated simulated resume has no cumulative size or position drift",placements.TakeLast(10).Distinct().Count()==1);
        }
        simulatedWindow.Close();
        var click=new OrbClick();click.Begin(new(36,36));check("One click accepted",click.End(new(36,36),true));check("Duplicate mouse-up ignored",!click.End(new(36,36),true));
        click.Begin(new(36,36));click.Move(new(60,36));check("Drag returning to origin ignored",!click.End(new(36,36),true));
        click.Begin(new(36,36));click.Cancel();check("Lost capture ignored",!click.End(new(36,36),true));
        click.Begin(new(36,36));check("Release outside ignored",!click.End(new(36,36),false));
        var toggles=0;for(var n=0;n<50;n++){click.Begin(new(36,36));if(click.End(new(36,36),true))toggles++;if(click.End(new(36,36),true))toggles++;}
        check("Fifty rapid clicks produce exactly fifty toggles despite duplicate releases",toggles==50);
        var sources=new List<FakeCapture>();var active=0;var maximum=0;var received=0;
        using(var capture=new CaptureRecovery(()=>{var source=new FakeCapture(()=>{active++;maximum=Math.Max(maximum,active);},()=>active--);sources.Add(source);return source;},_=>received++,()=>{}))
        {
            for(var i=0;i<30;i++){capture.Start();sources[^1].Push();capture.Stop();sources[^1].Push();}
            check("Rapid pause/resume has one capture source and rejects late frames",active==0&&maximum==1&&received==30&&!capture.DesiredRunning);
            capture.Start();var old=sources[^1];capture.Stop();old.Fail();
            check("Late capture failure cannot reopen after pause",active==0&&sources.Count==31);
        }
        var recoveryWait=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sources.Clear();active=maximum=received=0;
        using(var capture=new CaptureRecovery(()=>{var source=new FakeCapture(()=>{active++;maximum=Math.Max(maximum,active);},()=>active--);sources.Add(source);return source;},_=>received++,()=>{},async(_,token)=>{recoveryWait.TrySetResult();await recoveryRelease.Task.WaitAsync(token);}))
        {
            capture.Start();sources[^1].Push();sources[^1].Fail();await recoveryWait.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check("Simulated device interruption closes capture before recovery",active==0);
            recoveryRelease.TrySetResult();
            for(var n=0;n<100&&sources.Count<2;n++)await Task.Delay(10);
            sources[^1].Push();check("Recovery after simulated resume opens exactly one replacement",sources.Count==2&&active==1&&maximum==1&&capture.Health.Streaming);
            capture.Stop();sources[^1].Push();check("Pause after recovery closes source and rejects late data",active==0&&received==2&&!capture.Health.Requested);
        }
        var waiting=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sources.Clear();active=0;
        using(var capture=new CaptureRecovery(()=>{var source=new FakeCapture(()=>active++,()=>active--);sources.Add(source);return source;},_=>{},()=>{},async(_,token)=>{waiting.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);}finally{cancelled.TrySetResult();}}))
        {
            capture.Start();sources[0].Fail();await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));capture.Stop();await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check("Pause cancels pending resume recovery without reopening a loop",sources.Count==1&&active==0&&!capture.DesiredRunning);
        }
        c.Orb.Show();await Idle();
        c.ToggleCapture();await Idle();check("Demo monitoring enables",c.MonitoringEnabled);
        c.Pause();await Idle();check("Pause stops monitoring and preserves history",!c.MonitoringEnabled&&c.History.Count==0);
        for(var n=0;n<50;n++)
        {
            click.Begin(new(36,36));if(click.End(new(36,36),true))c.ToggleCapture();
            if(click.End(new(36,36),true))c.ToggleCapture();
            check($"Rapid gesture updates actual controller state {n}",c.MonitoringEnabled==(n%2==0));
        }
        c.SelectMode(StudyMode.Transcription);await c.StartRecordingAsync();var wasRecording=c.RecordingActive;
        c.SelectMode(StudyMode.Questions);
        check("Mode selection leaves active recording intact",wasRecording&&c.RecordingActive&&!c.MonitoringEnabled);
        await c.StopRecordingAsync();
        check("Recording stops through its existing dedicated action",!c.RecordingActive&&!c.MonitoringEnabled);
        c.SelectMode(StudyMode.Questions);
        // Ensure native DPI/resize notifications restore the canonical DIP content.
        c.Orb.Width=110;c.Orb.Height=50;await Idle();await Idle();
        check("Native resize restores unified panel DIP square",Math.Abs(c.Orb.ActualWidth-OrbWindow.PanelSize)<1&&Math.Abs(c.Orb.ActualHeight-OrbWindow.PanelSize)<1);
        UiSmoke.Capture(c.Orb,Path.Combine(dir,"overlay-canonical.png"));
    }
    private static async Task Idle()=>await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
    private sealed class FakeCapture(Action start,Action stop):IAudioCapture
    {
        private bool started;
        public event Action<short[]>? Frame;
        public event Action<CaptureFault>? Failed;
        public void Start(){started=true;start();}
        public void Push()=>Frame?.Invoke(new short[320]);
        public void Fail()=>Failed?.Invoke(new("fixture"));
        public void Dispose(){if(started){started=false;stop();}}
    }
}
