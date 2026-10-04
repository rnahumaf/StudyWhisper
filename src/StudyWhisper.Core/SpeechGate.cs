using System.Runtime.InteropServices;
namespace StudyWhisper.Core;

public record SpeechSegment(byte[] Wav,bool ForcedBoundary,long StartSample,long EndSample);

/// <summary>Streaming 20 ms capture → 32 ms probability blocks. No semantic audio filtering.</summary>
public sealed class SpeechGate : IDisposable
{
    private readonly ISpeechProbability model;
    private readonly SessionCounters counters;
    private readonly float start, keep;
    private readonly int maximum;
    private readonly short[] block=new short[Silero.Samples];
    private readonly Queue<short[]> preRoll=new();
    private readonly List<short> segment=new();
    private int fill,onset,quiet,noiseQuiet,noiseFrames;
    private bool active,noise,speechInEpisode;
    private long processed,segmentStart;
    public const int PreRollBlocks=10, EndSilenceBlocks=25, TailBlocks=8, OverlapBlocks=8;
    public double Level {get;private set;}
    public float Probability {get;private set;}
    public event Action<FlowKind>? Flow;
    public int BufferedSamples => fill+segment.Count+preRoll.Count*Silero.Samples;
    public SpeechGate(ISpeechProbability model,SessionCounters counters,double startProbability=.5,int maxSeconds=15)
    {
        if(!double.IsFinite(startProbability)||startProbability is <.3 or >.8||maxSeconds is <3 or >30) throw new ArgumentOutOfRangeException(nameof(startProbability));
        this.model=model;this.counters=counters;start=(float)startProbability;keep=Math.Max(.15f,start-.15f);
        maximum=maxSeconds*Vad.Rate/Silero.Samples*Silero.Samples;
    }
    public SpeechSegment? Push(ReadOnlySpan<short> pcm)
    {
        if(pcm.Length!=Vad.FrameSamples) throw new ArgumentException("Captura exige quadros de 20 ms.");
        counters.Monitor(pcm.Length);
        double sum=0;foreach(var x in pcm)sum+=(double)x*x;Level=Math.Sqrt(sum/pcm.Length)/32768;
        SpeechSegment? output=null;
        while(!pcm.IsEmpty)
        {
            var n=Math.Min(block.Length-fill,pcm.Length);pcm[..n].CopyTo(block.AsSpan(fill));fill+=n;pcm=pcm[n..];
            if(fill<block.Length)continue;
            processed+=block.Length;Probability=model.Predict(block);output=Process(block,Probability)??output;Array.Clear(block);fill=0;
        }
        return output;
    }
    private SpeechSegment? Process(short[] frame,float p)
    {
        // Count acoustic episodes rejected before STT; silence alone isn't a discarded "segment".
        double power=0;foreach(var x in frame)power+=(double)x*x;
        var audible=Math.Sqrt(power/frame.Length)/32768>=.005;
        if(audible){if(!noise)Flow?.Invoke(FlowKind.Candidate);noise=true;noiseQuiet=0;} else if(noise)noiseQuiet++;
        if(noise)noiseFrames++;
        if(p>=keep)counters.Speech(frame.Length);
        if(active&&p>=keep)speechInEpisode=true;
        if(noise && (noiseQuiet>=EndSilenceBlocks || noiseFrames>=Vad.Rate*30/Silero.Samples))
        { if(!speechInEpisode){counters.RejectLocal();Flow?.Invoke(FlowKind.LocalRejected);}noise=false;noiseQuiet=noiseFrames=0;speechInEpisode=false; }
        if(!active)
        {
            preRoll.Enqueue((short[])frame.Clone());while(preRoll.Count>PreRollBlocks)Array.Clear(preRoll.Dequeue());
            onset=p>=start?onset+1:0;
            if(onset<2)return null; // 64 ms of probability, no fixed minimum utterance duration.
            active=true;
            Flow?.Invoke(FlowKind.SpeechDetected);
            speechInEpisode=true;
            segmentStart=processed-preRoll.Count*Silero.Samples;
            while(preRoll.Count>0){var before=preRoll.Dequeue();segment.AddRange(before);Array.Clear(before);}
        }
        else segment.AddRange(frame);
        quiet=p>=keep?0:quiet+1;
        if(quiet>=EndSilenceBlocks)
        {
            var count=segment.Count-(quiet-TailBlocks)*Silero.Samples;
            var wav=Wav.Encode(CollectionsMarshal.AsSpan(segment)[..count]);ClearSegment();active=false;onset=quiet=0;
            Flow?.Invoke(FlowKind.LocalApproved);
            return new(wav,false,segmentStart,segmentStart+count);
        }
        if(segment.Count<maximum)return null;
        var result=Wav.Encode(CollectionsMarshal.AsSpan(segment));
        var output=new SpeechSegment(result,true,segmentStart,segmentStart+segment.Count);
        // Bounded cut with overlap: preserve boundary phonemes, retain recurrent model state.
        var overlap=segment.TakeLast(OverlapBlocks*Silero.Samples).ToArray();segmentStart+=segment.Count-overlap.Length;ClearSegment();segment.AddRange(overlap);Array.Clear(overlap);
        counters.Boundary();Flow?.Invoke(FlowKind.LocalApproved);return output;
    }
    private void ClearSegment(){CollectionsMarshal.AsSpan(segment).Clear();segment.Clear();}
    public void Reset()
    {
        // A cancelled candidate is not a completed non-speech rejection.
        if(active||noise)counters.DiscardBuffered();
        ClearSegment();while(preRoll.Count>0)Array.Clear(preRoll.Dequeue());Array.Clear(block);model.Reset();
        fill=onset=quiet=noiseQuiet=noiseFrames=0;active=noise=speechInEpisode=false;Level=Probability=0;
        processed=segmentStart=0;
    }
    public void Dispose(){Reset();model.Dispose();}
}
