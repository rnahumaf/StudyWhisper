using System.Diagnostics;
namespace StudyWhisper.App;

public record CaptureFault(string Operation,uint Code=0,string Category="native",bool Recoverable=true);
public sealed class CaptureException(CaptureFault fault) : Exception("Falha na entrada de áudio.") {public CaptureFault Fault {get;}=fault;}
public interface IAudioCapture : IDisposable
{
    event Action<short[]>? Frame;
    event Action<CaptureFault>? Failed;
    void Start();
}
public enum CaptureState { Ready, Recovering, Blocked, Starting }
public record CaptureStatus(CaptureState State,int Attempt=0,TimeSpan? Delay=null,CaptureFault? Fault=null);
public record CaptureHealth(long Generation,bool Requested,bool Streaming,long Frames,long Samples,double LastFrameAgeMs,int SourceOpens);

/// <summary>One local source at a time. Ready means frames have arrived, not merely that a handle opened.</summary>
public sealed class CaptureRecovery : IDisposable
{
    private readonly Func<IAudioCapture> factory;
    private readonly Action<short[]> frame;
    private readonly Action reset;
    private readonly Func<TimeSpan,CancellationToken,Task> delay;
    private readonly object sync=new();
    private readonly Timer watchdog;
    private CancellationTokenSource? lifetime;
    private IAudioCapture? source;
    private long epoch,openedAt,lastFrame,frames,samples;
    private int failures,sourceOpens;
    private bool desired,recovering,frameReady,blocked,disposed;
    public event Action<CaptureStatus>? Changed;
    public CaptureRecovery(Func<IAudioCapture> factory,Action<short[]> frame,Action reset,Func<TimeSpan,CancellationToken,Task>? wait=null)
    {this.factory=factory;this.frame=frame;this.reset=reset;delay=wait??Task.Delay;watchdog=new(_=>CheckFrames(),null,Timeout.Infinite,Timeout.Infinite);}
    public bool DesiredRunning {get{lock(sync)return desired;}}
    public long Generation {get{lock(sync)return epoch;}}
    public CaptureHealth Health
    {
        get{lock(sync){var age=lastFrame==0?-1:Stopwatch.GetElapsedTime(lastFrame).TotalMilliseconds;return new(epoch,desired,desired&&source is not null&&frameReady&&age>=0&&age<2000,frames,samples,age,sourceOpens);}}
    }
    public void Start()
    {
        Stop();long generation;CancellationToken token;
        lock(sync){ObjectDisposedException.ThrowIf(disposed,this);desired=true;blocked=false;failures=0;lifetime=new();token=lifetime.Token;generation=epoch;watchdog.Change(500,500);}
        Notify(generation,new(CaptureState.Starting));var fault=Open(generation);if(fault is not null)Schedule(generation,token,fault);
    }
    private CaptureFault? Open(long generation)
    {
        lock(sync)
        {
            if(!desired||generation!=epoch)return null;
            IAudioCapture? next=null;
            try
            {
                reset();next=factory();source=next;sourceOpens++;frameReady=false;lastFrame=0;openedAt=Stopwatch.GetTimestamp();
                next.Frame+=pcm=>
                {
                    bool announce;
                    lock(sync)
                    {
                        if(!desired||source!=next||generation!=epoch)return;
                        frames++;samples+=pcm.Length;lastFrame=Stopwatch.GetTimestamp();announce=!frameReady;frameReady=true;
                    }
                    if(announce)Notify(generation,new(CaptureState.Ready));frame(pcm);
                };
                next.Failed+=fault=>{CancellationToken token;lock(sync){if(!desired||source!=next||generation!=epoch)return;token=lifetime!.Token;}Schedule(generation,token,fault,next);};
                next.Start();return null;
            }
            catch(Exception ex)
            {
                source=null;next?.Dispose();
                return ex is CaptureException capture?capture.Fault:new("iniciar",Category:ex.GetType().Name,Recoverable:ex is not (OutOfMemoryException or NativeDependencyException));
            }
        }
    }
    private void CheckFrames()
    {
        long generation;CancellationToken token;IAudioCapture? expected;
        lock(sync)
        {
            if(disposed||!desired||blocked||recovering||source is null)return;
            if(Stopwatch.GetElapsedTime(lastFrame==0?openedAt:lastFrame)<TimeSpan.FromSeconds(2))return;
            generation=epoch;token=lifetime!.Token;expected=source;
        }
        Schedule(generation,token,new("frames-ausentes",Category:"watchdog"),expected);
    }
    private void Schedule(long generation,CancellationToken token,CaptureFault fault,IAudioCapture? expected=null)
    {
        lock(sync)
        {
            if(!desired||epoch!=generation||recovering||expected is not null&&!ReferenceEquals(source,expected))return;
            recovering=true;
            if(frameReady&&Stopwatch.GetElapsedTime(openedAt)>TimeSpan.FromSeconds(30))failures=0;
        }
        _=Task.Run(()=>RecoverAsync(generation,token,fault));
    }
    private async Task RecoverAsync(long generation,CancellationToken token,CaptureFault fault)
    {
        try
        {
            CloseSource(generation);
            while(true)
            {
                token.ThrowIfCancellationRequested();int attempt;
                lock(sync){if(!desired||generation!=epoch)return;attempt=++failures;}
                if(!fault.Recoverable){lock(sync){if(generation!=epoch)return;blocked=true;}Notify(generation,new(CaptureState.Blocked,attempt,Fault:fault));return;}
                var seconds=attempt switch{1=>.1,2=>.25,3=>.5,4=>1,5=>2,_=>5};
                var duration=TimeSpan.FromSeconds(seconds);Notify(generation,new(CaptureState.Recovering,attempt,duration,fault));
                await delay(duration,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
                Notify(generation,new(CaptureState.Starting,attempt));var nextFault=Open(generation);
                if(nextFault is null)return;fault=nextFault;
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        finally{lock(sync)if(generation==epoch)recovering=false;}
    }
    private void Notify(long generation,CaptureStatus status){lock(sync)if(desired&&generation==epoch)Changed?.Invoke(status);}
    private void CloseSource(long generation){IAudioCapture? previous;lock(sync){if(generation!=epoch)return;previous=source;source=null;frameReady=false;}previous?.Dispose();}
    public void Stop()
    {
        IAudioCapture? previous;CancellationTokenSource? cancel;
        lock(sync){desired=false;epoch++;recovering=frameReady=false;previous=source;source=null;cancel=lifetime;lifetime=null;if(!disposed)watchdog.Change(Timeout.Infinite,Timeout.Infinite);}
        cancel?.Cancel();previous?.Dispose();cancel?.Dispose();
    }
    public void Dispose(){Stop();lock(sync){if(disposed)return;disposed=true;watchdog.Dispose();}}
}
