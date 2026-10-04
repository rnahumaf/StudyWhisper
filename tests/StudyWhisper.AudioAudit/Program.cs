using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using StudyWhisper.Core;
using StudyWhisper.TestAudio;
using Microsoft.Win32;
string CpuModel(){if(OperatingSystem.IsWindows()){using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");return key?.GetValue("ProcessorNameString")?.ToString()??"Unknown";}return "Unknown";}
var output=args.Length>0?args[0]:"audio-triage-audit.json";
var fixtureDir=args.Length>1?args[1]:Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../fixtures"));
var scenarios=new List<(string Name,short[] Pcm,double Gain)>();
short[] Tone(int seconds,double hz=220){var data=new short[seconds*16000];for(var i=0;i<data.Length;i++)data[i]=(short)(6000*Math.Sin(2*Math.PI*hz*i/16000));return data;}
scenarios.Add(("silence-60s",new short[60*16000],1));scenarios.Add(("tone-220Hz-60s",Tone(60),1));scenarios.Add(("tone-1000Hz-60s",Tone(60,1000),1));
var random=new Random(7384);var noise=new short[30*16000];for(var i=0;i<noise.Length;i++)noise[i]=(short)random.Next(-8000,8001);scenarios.Add(("white-noise-30s",noise,1));
var clicks=new short[30*16000];for(var i=0;i<clicks.Length;i+=1600)for(var j=0;j<8;j++)clicks[i+j]=(short)(j%2==0?25000:-25000);scenarios.Add(("clicks-30s",clicks,1));
foreach(var name in new[]{"pt-BR-question","pt-BR-short-question","pt-BR-comment"})
{
    var pcm=AudioFixtures.Load(Path.Combine(fixtureDir,name+".wav"));scenarios.Add((name,pcm,1));
    if(name=="pt-BR-short-question")scenarios.Add((name+"-gain-0.1",pcm,.1));
}
var results=new List<object>();
SpectrumAudit.Write(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"spectrum-audit.json"));
foreach(var scenario in scenarios)
{
    var counters=new SessionCounters();using var silero=new MeasuredProbability(new Silero());using var gate=new SpeechGate(silero,counters);
    var legacy=new Vad();var legacySeconds=new List<double>();var neuralSeconds=new List<double>();var ranges=new List<(long Start,long End)>();var peak=0;
    var cpuStart=Process.GetCurrentProcess().TotalProcessorTime;
    foreach(var frame in AudioFixtures.Frames(scenario.Pcm,gain:scenario.Gain))
    {
        var before=legacy.Push(frame);if(before is not null){legacySeconds.Add((before.Length-44)/32000.0);Array.Clear(before);}
        var after=gate.Push(frame);if(after is not null){neuralSeconds.Add((after.Wav.Length-44)/32000.0);ranges.Add((after.StartSample,after.EndSample));Array.Clear(after.Wav);}
        peak=Math.Max(peak,gate.BufferedSamples);Array.Clear(frame);
    }
    var cpuMs=(Process.GetCurrentProcess().TotalProcessorTime-cpuStart).TotalMilliseconds;
    var times=silero.Times.Skip(10).OrderBy(x=>x).ToArray();
    var uncovered=0;for(var sample=0;sample<scenario.Pcm.Length;sample++)if(Math.Abs((int)scenario.Pcm[sample])>16&&!ranges.Any(r=>sample+16000>=r.Start&&sample+16000<r.End))uncovered++;
    results.Add(new{scenario=scenario.Name,inputSeconds=scenario.Pcm.Length/16000.0,gain=scenario.Gain,energy=new{segments=legacySeconds.Count,exportedSeconds=legacySeconds.Sum()},
        silero=new{segments=neuralSeconds.Count,exportedSeconds=neuralSeconds.Sum(),segmentSeconds=neuralSeconds,uncoveredNonzeroSamples=uncovered,peakBufferedSamples=peak,session=counters.Snapshot(),
            inference=new{frames=times.Length,meanMs=times.Average(),p95Ms=times[(int)(times.Length*.95)],maxMs=times[^1],probabilityMax=silero.MaxProbability},processCpuMs=cpuMs,processCpuMsPerInferenceBlock=cpuMs/silero.Times.Count}});
}
var json=JsonSerializer.Serialize(new{modelVersion="Silero VAD 6.2.3, 16k/op15",modelSha256=Silero.ModelHash,onnxRuntime="1.30.0 CPU, one thread",machine=new{os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),logicalProcessors=Environment.ProcessorCount,cpuModel=CpuModel()},method="Offline deterministic fixtures; first 10 inference blocks omitted from wall-time metrics. Process CPU includes legacy VAD, framing, gate and inference; no HTTP/microphone. CPU name from read-only registry; CIM unavailable in sandbox.",results},new JsonSerializerOptions{WriteIndented=true});
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json);Console.WriteLine(json);
sealed class MeasuredProbability(ISpeechProbability inner):ISpeechProbability
{
    public List<double> Times=new();public float MaxProbability;
    public float Predict(ReadOnlySpan<short> pcm){var time=Stopwatch.GetTimestamp();var result=inner.Predict(pcm);Times.Add(Stopwatch.GetElapsedTime(time).TotalMilliseconds);MaxProbability=Math.Max(MaxProbability,result);return result;}
    public void Reset()=>inner.Reset();public void Dispose()=>inner.Dispose();
}
