using System.Buffers.Binary;
namespace StudyWhisper.Core;

/// <summary>Legacy energy VAD, retained only for offline comparison and demo fixtures. Production uses SpeechGate/Silero.</summary>
public sealed class Vad
{
    public const int Rate = 16000, FrameSamples = 320;
    private readonly Queue<short[]> preRoll = new();
    private readonly List<short> segment = new();
    private readonly double threshold;
    private readonly int maxFrames;
    private int speech, quiet, frames;
    private bool active;
    public Vad(double threshold = 0.018, int maxSeconds = 15)
    {
        if (!double.IsFinite(threshold) || threshold is < 0.002 or > 0.2 || maxSeconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(threshold));
        this.threshold = threshold; maxFrames = maxSeconds * 50;
    }
    public double Level { get; private set; }
    public int BufferedSamples => segment.Count + preRoll.Count * FrameSamples;
    public byte[]? Push(ReadOnlySpan<short> pcm)
    {
        if (pcm.Length != FrameSamples) throw new ArgumentException("VAD exige quadros de 20 ms.");
        double sum = 0; foreach (var sample in pcm) sum += (double)sample * sample;
        Level = Math.Sqrt(sum / pcm.Length) / 32768.0;
        var voiced = Level >= threshold;
        var frame = pcm.ToArray();
        if (!active)
        {
            preRoll.Enqueue(frame); while (preRoll.Count > 10) Array.Clear(preRoll.Dequeue());
            speech = voiced ? speech + 1 : 0;
            if (speech < 3) return null;
            active = true; frames = preRoll.Count;
            while (preRoll.Count > 0) { var p = preRoll.Dequeue(); segment.AddRange(p); Array.Clear(p); }
        }
        else { segment.AddRange(frame); Array.Clear(frame); frames++; if (voiced) speech++; }
        quiet = voiced ? 0 : quiet + 1;
        if (quiet < 35 && frames < maxFrames) return null;
        var result = speech >= 10 ? Wav.Encode(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(segment)) : null;
        Reset(); return result;
    }
    public void Reset()
    {
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(segment).Clear(); segment.Clear();
        while (preRoll.Count > 0) Array.Clear(preRoll.Dequeue());
        speech = quiet = frames = 0; active = false; Level = 0;
    }
}
public static class Wav
{
    public static byte[] Encode(ReadOnlySpan<short> pcm)
    {
        var data = new byte[44 + pcm.Length * 2];
        "RIFF"u8.CopyTo(data); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), data.Length - 8);
        "WAVEfmt "u8.CopyTo(data.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(20),1); BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22),1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24),Vad.Rate); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(28),Vad.Rate*2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(32),2); BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(34),16);
        "data"u8.CopyTo(data.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(40),pcm.Length*2);
        for (int i=0;i<pcm.Length;i++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(44+i*2),pcm[i]);
        return data;
    }
    public static IEnumerable<short[]> Synthetic(int voicedFrames = 50, int silenceFrames = 40)
    {
        for(int f=0;f<voicedFrames+silenceFrames;f++)
        {
            var frame=new short[Vad.FrameSamples];
            if(f<voicedFrames) for(int i=0;i<frame.Length;i++) frame[i]=(short)(6000*Math.Sin(2*Math.PI*220*(f*frame.Length+i)/Vad.Rate));
            yield return frame;
        }
    }
}
