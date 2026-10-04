namespace StudyWhisper.Core;
public sealed class PopoverClock
{
    public TimeSpan Remaining { get; private set; }
    public void Reset(int seconds)=>Remaining=TimeSpan.FromSeconds(seconds);
    public bool Tick(TimeSpan elapsed,bool interacting)
    {
        if(!interacting) Remaining-=elapsed;
        return Remaining<=TimeSpan.Zero;
    }
}
