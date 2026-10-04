using System.Text.Json;
namespace StudyWhisper.App;
/// <summary>Simulated loader faults only; no machine modification, settings, credential, microphone or HTTP.</summary>
public static class DependencySmoke
{
    public static int Run(string[] args)
    {
        var checks=new List<object>();var factoryCalls=0;
        try
        {
            try{using var unavailable=NativeRuntime.Create(NativeRuntime.Check(_=>false),()=>{factoryCalls++;throw new InvalidOperationException("Should not load");});}
            catch(NativeDependencyException ex){checks.Add(new{name="Missing CRT blocked before native model/device creation",passed=factoryCalls==0&&ex.Problem.UnavailableLibraries.Count==4});checks.Add(new{name="Actionable Microsoft x64 repair advice",passed=ex.Message.Contains("Microsoft Visual C++")&&ex.Message.Contains("x64")&&ex.Message.Contains("pausado")&&NativeRuntime.HelpUri.Host=="learn.microsoft.com"});}
            try{using var incompatible=NativeRuntime.Create(NativeRuntime.Check(_=>true),()=>throw new TypeInitializationException("fixture",new DllNotFoundException("SIMULATED-PRIVATE-DETAIL")));}
            catch(NativeDependencyException ex){checks.Add(new{name="Wrapped native loader failure handled without raw DLL error",passed=ex.Message.Contains("ONNX")&&!ex.Message.Contains("SIMULATED-PRIVATE-DETAIL")});}
        }
        catch(Exception){checks.Add(new{name="Dependency smoke failure",passed=false});}
        var i=Array.IndexOf(args,"--output");if(i>=0&&i+1<args.Length){Directory.CreateDirectory(args[i+1]);File.WriteAllText(Path.Combine(args[i+1],"dependency-checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
        return checks.Count==3&&checks.All(c=>JsonSerializer.SerializeToElement(c).GetProperty("passed").GetBoolean())?0:1;
    }
}
