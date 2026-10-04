using System.Diagnostics;
using System.Text.Json;
using StudyWhisper.Core;

static class SpectrumAudit
{
    public static void Write(string path)
    {
        var pcm=new short[60*16000];for(var n=0;n<pcm.Length;n++)pcm[n]=(short)(7000*Math.Sin(2*Math.PI*125*n/16000)+5000*Math.Sin(2*Math.PI*1000*n/16000)+3000*Math.Sin(2*Math.PI*4000*n/16000));
        var spectrum=new Spectrum();for(var n=0;n<32000;n+=320)spectrum.Push(pcm.AsSpan(n,320));spectrum.Reset();
        var analyses=spectrum.Analyses;var times=new double[600];var index=0;using var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var elapsed=Stopwatch.StartNew();var allocated=GC.GetAllocatedBytesForCurrentThread();
        for(var n=0;n<pcm.Length;n+=320){var start=Stopwatch.GetTimestamp();var value=spectrum.Push(pcm.AsSpan(n,320));if(value.HasValue)times[index++]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}
        var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;elapsed.Stop();var cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds;Array.Sort(times);Array.Clear(pcm);spectrum.Reset();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(new{input="60 seconds synthetic 125/1000/4000 Hz PCM16 16k, no microphone",frames=3000,analyses=spectrum.Analyses-analyses,analysisPerSecond=10,fftSize=1024,windowMs=64,hopMs=100,analysisFrameMeanMs=times.Average(),analysisFrameP95Ms=times[570],analysisFrameMaxMs=times[^1],elapsedMs=elapsed.Elapsed.TotalMilliseconds,processCpuMs=cpuMs,cpuMsPerAudioSecond=cpuMs/60,managedBytesAllocatedInStreamingLoop=bytes,persistentNumericBufferBytes=1024*(2+8*3),note="Warm reference-machine CPU test. Includes ring-buffer writes in FFT-triggering frames. Does not benchmark a low-end laptop, GPU/composition, real microphone or other DPI monitors."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
