using System.Runtime.InteropServices;
namespace StudyWhisper.Core;
public record NativeReadiness(bool Available,IReadOnlyList<string> UnavailableLibraries,string Message)
{
    public static NativeReadiness Ready => new(true,Array.Empty<string>(),"");
}
public sealed class NativeDependencyException(NativeReadiness problem,Exception? inner=null):Exception(problem.Message,inner)
{
    public NativeReadiness Problem {get;}=problem;
}
/// <summary>Read-only dependency probe. No downloads, installers, registry writes, credentials or devices.</summary>
public static class NativeRuntime
{
    public static readonly Uri HelpUri=new("https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170");
    // Confirmed from PE imports of official ONNX Runtime 1.30.0 win-x64.
    public static IReadOnlyList<string> CrtLibraries {get;}=Array.AsReadOnly(new[]{"MSVCP140.dll","MSVCP140_1.dll","VCRUNTIME140.dll","VCRUNTIME140_1.dll"});
    private const string Advice="O filtro de áudio precisa do Microsoft Visual C++ v14 Redistributable x64. O runtime está ausente ou não carrega corretamente. Instale ou repare a versão x64 atual pelo site oficial da Microsoft e reabra o StudyWhisper. Se o problema continuar, reinstale o StudyWhisper. O monitoramento permanece pausado; o modo demonstração continua disponível. Este app não baixa nem instala o runtime.";
    public static NativeReadiness Check(Func<string,bool>? canLoad=null)
    {
        canLoad??=CanLoad;var missing=CrtLibraries.Where(name=>!canLoad(name)).ToArray();
        return missing.Length==0?NativeReadiness.Ready:new(false,missing,Advice);
    }
    private static bool CanLoad(string name)
    {
        if(!OperatingSystem.IsWindows())return false;
        // Absolute app/system locations only; no CWD or arbitrary PATH entries.
        foreach(var folder in new[]{AppContext.BaseDirectory,Environment.SystemDirectory}.Distinct())
        {
            try
            {
                var path=Path.Combine(folder,name);
                if(!File.Exists(path)||!NativeLibrary.TryLoad(path,out var handle))continue;
                NativeLibrary.Free(handle);return true;
            }
            catch(Exception e) when(e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException){}
        }
        return false;
    }
    public static bool IsLoaderFailure(Exception error)
    {
        for(Exception? e=error;e is not null;e=e.InnerException)
            if(e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)return true;
        return false;
    }
    /// <summary>Validate native imports/exports with synthetic silence. Returned detector is reset.</summary>
    public static ISpeechProbability Create(NativeReadiness readiness,Func<ISpeechProbability>? factory=null)
    {
        if(!readiness.Available)throw new NativeDependencyException(readiness);
        ISpeechProbability? detector=null;
        try
        {
            detector=(factory??(()=>new Silero()))();detector.Predict(new short[Silero.Samples]);detector.Reset();return detector;
        }
        catch(Exception ex) when(IsLoaderFailure(ex))
        {
            detector?.Dispose();throw new NativeDependencyException(new(false,Array.Empty<string>(),"O componente ONNX do filtro de áudio não carregou: há uma biblioteca ausente ou incompatível. Atualize ou repare o Microsoft Visual C++ v14 Redistributable x64 pelo site oficial. Se o problema continuar, reinstale o StudyWhisper. O monitoramento permanece pausado; o modo demonstração continua disponível. Este app não baixa nem instala o runtime."),ex);
        }
        catch {detector?.Dispose();throw;}
    }
}
