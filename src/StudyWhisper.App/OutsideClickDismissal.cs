using System.Runtime.InteropServices;
using System.Windows.Threading;
namespace StudyWhisper.App;

/// <summary>Observes button-down only while a transient surface is open. Does not intercept clicks or retain input.</summary>
public sealed class OutsideClickDismissal : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct MouseData {public Point Point;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int kind,Hook hook,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    private readonly Hook callback;
    private readonly Func<IEnumerable<IntPtr>> inside;
    private readonly Action close;
    private readonly Dispatcher dispatcher;
    private IntPtr handle;
    private bool queued,disposed;
    public OutsideClickDismissal(Func<IEnumerable<IntPtr>> inside,Action close)
    {
        this.inside=inside;this.close=close;dispatcher=Dispatcher.CurrentDispatcher;callback=Observe;
        handle=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
        // WPF/WinForms still provide their normal Escape/deactivation behavior if a desktop forbids hooks.
    }
    private IntPtr Observe(int code,IntPtr message,IntPtr data)
    {
        try
        {
            if(code>=0&&!disposed&&!queued&&message.ToInt64() is 0x201 or 0x204 or 0x207 or 0x20B)
            {
                var point=Marshal.PtrToStructure<MouseData>(data).Point;
                if(!inside().Any(hwnd=>hwnd!=IntPtr.Zero&&GetWindowRect(hwnd,out var rect)&&point.X>=rect.Left&&point.X<rect.Right&&point.Y>=rect.Top&&point.Y<rect.Bottom))
                {
                    queued=true;dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{if(!disposed)close();}));
                }
            }
        }
        catch(InvalidOperationException){} // A surface can close while its final native input is being delivered.
        return CallNextHookEx(handle,code,message,data);
    }
    public void Dispose(){if(disposed)return;disposed=true;if(handle!=IntPtr.Zero)UnhookWindowsHookEx(handle);handle=IntPtr.Zero;}
}
