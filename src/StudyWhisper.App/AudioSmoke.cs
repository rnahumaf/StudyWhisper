using System.Text.Json;
namespace StudyWhisper.App;

/// <summary>Packaged ONNX/native DLL/embedded model check; never creates a Controller or device.</summary>
public static class AudioSmoke
{
    public static int Run(string[] args)
    {
        var checks=new List<object>();
        try
        {
            using var silero=NativeRuntime.Create(NativeRuntime.Check());using var gate=new SpeechGate(silero,new());
            for(var f=0;f<100;f++)if(gate.Push(new short[320]) is not null)throw new InvalidOperationException("Silence triggered speech");
            checks.Add(new{name="Embedded model hash and native ONNX CPU inference",passed=true});
            checks.Add(new{name="No segment emitted for synthetic silence",passed=true});
        }
        catch(Exception ex){checks.Add(new{name="Packaged audio smoke",passed=false,error=ex.Message});}
        var i=Array.IndexOf(args,"--output");if(i>=0&&i+1<args.Length){Directory.CreateDirectory(args[i+1]);File.WriteAllText(Path.Combine(args[i+1],"audio-checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
        return checks.Count==2?0:1;
    }
}
