using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
namespace StudyWhisper.App;

// Monitor rectangles and SetWindowPos are physical pixels; WPF content stays 72 DIP.
internal sealed class MonitorAnchor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Info {public int Size;public Rect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor,ref Info info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(System.Drawing.Point point,uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    private readonly Window window;
    private HwndSource? source;
    private string? device;
    private bool queued,disposed;
    internal sealed record Display(string Device,bool Primary,System.Drawing.Rectangle Work,uint Dpi);
    private readonly Func<Display[]>? topology;
    private readonly Action<System.Drawing.Rectangle>? apply;
    internal int ApplyCount {get;private set;}
    internal string? Device=>device;
    public MonitorAnchor(Window window,Func<Display[]>? topology=null,Action<System.Drawing.Rectangle>? apply=null)
    {
        this.window=window;this.topology=topology;this.apply=apply;window.SourceInitialized+=Initialized;window.SizeChanged+=Resized;
        SystemEvents.DisplaySettingsChanged+=Changed;SystemEvents.PowerModeChanged+=Power;
    }
    internal static System.Drawing.Rectangle Bounds(System.Drawing.Rectangle work,uint dpi)
    {
        var size=Math.Min(Math.Min(work.Width,work.Height),Math.Max(1,(int)Math.Round(72*dpi/96.0)));
        var gap=(int)Math.Round(20*dpi/96.0);
        return new(Math.Max(work.Left,work.Right-size-gap),Math.Min(work.Bottom-size,work.Top+gap),size,size);
    }
    internal static string Select(string? current,(string Device,bool Primary)[] screens)=>screens.FirstOrDefault(s=>s.Device==current).Device??screens.FirstOrDefault(s=>s.Primary).Device??screens[0].Device;
    private void Initialized(object? sender,EventArgs e){source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source.AddHook(Hook);Request();}
    private void Changed(object? sender,EventArgs e)=>Request();
    private void Power(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume)Notify(0x0218);}
    private void Resized(object sender,SizeChangedEventArgs e)=>Request();
    private IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
    {
        // Let WPF process WM_DPICHANGED first, then restore canonical geometry.
        Notify(msg);
        return IntPtr.Zero;
    }
    internal void Notify(int message){if(message is 0x02E0 or 0x007E or 0x001A or 0x0218)Request();}
    internal void Request()
    {
        if(disposed)return;
        if(!window.Dispatcher.CheckAccess()){window.Dispatcher.BeginInvoke(Request);return;}
        if(queued)return;queued=true;
        window.Dispatcher.BeginInvoke(()=>{queued=false;if(!disposed&&(source is not null||topology is not null))Place();},DispatcherPriority.ContextIdle);
    }
    private void Place()
    {
        if(topology is not null)
        {
            var displays=topology();device=Select(device,displays.Select(s=>(s.Device,s.Primary)).ToArray());
            var display=displays.First(s=>s.Device==device);ApplyCount++;apply!(Bounds(display.Work,display.Dpi));return;
        }
        var screens=System.Windows.Forms.Screen.AllScreens;
        var selected=Select(device,screens.Select(s=>(s.DeviceName,s.Primary)).ToArray());
        var screen=screens.First(s=>s.DeviceName==selected);
        device=screen.DeviceName;
        var monitor=MonitorFromPoint(new(screen.Bounds.Left+screen.Bounds.Width/2,screen.Bounds.Top+screen.Bounds.Height/2),2);
        var info=new Info{Size=Marshal.SizeOf<Info>(),Device=""};if(!GetMonitorInfo(monitor,ref info))return;
        if(GetDpiForMonitor(monitor,0,out var dpi,out _)!=0||dpi==0)dpi=96;
        var bounds=Bounds(System.Drawing.Rectangle.FromLTRB(info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom),dpi);
        window.Width=window.Height=72;
        SetWindowPos(source!.Handle,IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0014);ApplyCount++;
    }
    public void Dispose(){disposed=true;window.SourceInitialized-=Initialized;window.SizeChanged-=Resized;SystemEvents.DisplaySettingsChanged-=Changed;SystemEvents.PowerModeChanged-=Power;source?.RemoveHook(Hook);}
}

internal sealed class OrbClick
{
    private Point? down;
    private bool moved;
    public void Begin(Point point){down=point;moved=false;}
    public void Move(Point point){if(down is Point start&&(Math.Abs(point.X-start.X)>=SystemParameters.MinimumHorizontalDragDistance||Math.Abs(point.Y-start.Y)>=SystemParameters.MinimumVerticalDragDistance))moved=true;}
    public bool End(Point point,bool inside){Move(point);var click=down.HasValue&&!moved&&inside;Cancel();return click;}
    public void Cancel(){down=null;moved=false;}
}
