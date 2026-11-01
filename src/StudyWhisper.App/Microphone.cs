using System.Runtime.InteropServices;
namespace StudyWhisper.App;

/// <summary>WaveIn event mode: no WinMM calls from a native callback; four fixed PCM buffers.</summary>
public sealed class Microphone : IAudioCapture
{
    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct Format { public ushort Tag,Channels; public uint Rate,BytesPerSecond; public ushort Align,Bits,Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public IntPtr Data; public uint Length,Recorded; public UIntPtr User; public uint Flags,Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);
    private readonly AutoResetEvent ready=new(false);
    private readonly List<IntPtr> buffers=new();
    private readonly uint size=(uint)Marshal.SizeOf<Header>();
    private IntPtr handle;
    private volatile bool stopped;
    private bool disposed;
    private Task? worker;
    public event Action<short[]>? Frame;
    public event Action<CaptureFault>? Failed;
    private static void Check(uint error,string operation) { if(error!=0) throw new CaptureException(new(operation,error,Recoverable:error is not (11 or 32))); }
    public void Start()
    {
        if(handle!=IntPtr.Zero) throw new InvalidOperationException("Captura já iniciada.");
        var format=new Format{Tag=1,Channels=1,Rate=Vad.Rate,BytesPerSecond=Vad.Rate*2,Align=2,Bits=16};
        try
        {
            Check(waveInOpen(out handle,uint.MaxValue,ref format,ready.SafeWaitHandle.DangerousGetHandle(),IntPtr.Zero,0x00050000),"abrir");
            for(int i=0;i<4;i++)
            {
                var pointer=Marshal.AllocHGlobal((int)size); var h=new Header{Data=Marshal.AllocHGlobal(Vad.FrameSamples*2),Length=Vad.FrameSamples*2};
                Marshal.StructureToPtr(h,pointer,false); buffers.Add(pointer); Check(waveInPrepareHeader(handle,pointer,size),"preparar"); Check(waveInAddBuffer(handle,pointer,size),"enfileirar");
            }
            Check(waveInStart(handle),"iniciar"); worker=Task.Run(ReadLoop);
        }
        catch { Dispose(); throw; }
    }
    private void ReadLoop()
    {
        try
        {
            while(!stopped)
            {
                ready.WaitOne(100); if(stopped) break;
                foreach(var p in buffers)
                {
                    var h=Marshal.PtrToStructure<Header>(p); if((h.Flags&1)==0) continue;
                    var frame=new short[Vad.FrameSamples];
                    try { if(h.Recorded==Vad.FrameSamples*2) { Marshal.Copy(h.Data,frame,0,frame.Length); Frame?.Invoke(frame); } }
                    finally {Array.Clear(frame);}
                    if(!stopped)Check(waveInAddBuffer(handle,p,size),"reenfileirar");
                }
            }
        }
        catch(Exception ex) { if(!stopped) Failed?.Invoke(ex is CaptureException capture?capture.Fault:new("processar-quadro",Category:ex.GetType().Name,Recoverable:ex is not (OutOfMemoryException or NativeDependencyException))); }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        stopped=true; ready.Set(); worker?.GetAwaiter().GetResult(); worker=null;
        if(handle!=IntPtr.Zero) waveInReset(handle);
        foreach(var p in buffers)
        {
            var h=Marshal.PtrToStructure<Header>(p);
            if(handle!=IntPtr.Zero) waveInUnprepareHeader(handle,p,size);
            for(int i=0;i<h.Length;i++) Marshal.WriteByte(h.Data,i,0);
            Marshal.FreeHGlobal(h.Data); Marshal.FreeHGlobal(p);
        }
        buffers.Clear(); if(handle!=IntPtr.Zero) waveInClose(handle); handle=IntPtr.Zero; ready.Dispose();
    }
}
