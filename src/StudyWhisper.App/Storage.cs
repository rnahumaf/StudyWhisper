using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using StudyWhisper.Core;
namespace StudyWhisper.App;

public sealed record UpdateSession(Turn[] History,long UsedCalls,bool WasListening,SessionSnapshot? CounterState=null);

public sealed class Storage(string root)
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StudyWhisper");
    public bool HasKey=>File.Exists(Path.Combine(root,"openrouter.dpapi"));
    public void DiscardLegacyRewriteWork()
    {
        var folder=Path.Combine(root,"rewrite-work");
        if(!Directory.Exists(folder))return;
        if(File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A pasta antiga de reescritas foi redirecionada.");
        foreach(var path in Directory.EnumerateFiles(folder,"*.jsonl"))
        {
            var name=Path.GetFileNameWithoutExtension(path);
            if(name.Length==64&&name.All(Uri.IsHexDigit))File.Delete(path);
        }
        if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
    }
    public Settings Load()
    {
        try { var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"settings.json")))??new(); s.Validate(); return s; }
        catch(Exception ex) when(ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(Settings s) { s.Validate(); Directory.CreateDirectory(root); Atomic(Path.Combine(root,"settings.json"),JsonSerializer.SerializeToUtf8Bytes(s,new JsonSerializerOptions{WriteIndented=true})); }
    public void RecordCaptureFault(string state,int attempt,string operation,uint code,string category)
    {
        // Bounded local metadata only: never exception messages, device names, PCM, transcript, key or answer.
        try
        {
            Directory.CreateDirectory(root);var file=Path.Combine(root,"capture-diagnostics.log");
            if(File.Exists(file)&&new FileInfo(file).Length>32_768)File.Move(file,file+".previous",true);
            File.AppendAllText(file,$"{DateTimeOffset.UtcNow:O} capture {state} attempt={attempt} operation={operation} code={code} category={category}\n");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
    }
    private static void Atomic(string file,byte[] data) { File.WriteAllBytes(file+".tmp",data); File.Move(file+".tmp",file,true); }
    public UpdateSession? ReadUpdateSession()
    {
        // Only an explicit local update handoff is persisted. Normal history remains RAM-only.
        var file=Path.Combine(root,"update-session.dpapi");if(!File.Exists(file))return null;byte[]? plain=null;
        try
        {
            if(new FileInfo(file).Length>1_000_000)return null;
            plain=Protect(File.ReadAllBytes(file),true);var state=JsonSerializer.Deserialize<UpdateSession>(plain);
            if(state is null||state.UsedCalls<0||state.History is null||state.History.Length>30||state.History.Any(t=>t.Question is null||t.Answer is null||t.Question.Length>4000||t.Answer.Length>12000))return null;
            return state;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception){return null;}
        finally{if(plain is not null)Array.Clear(plain);}
    }
    public void CompleteUpdateSession(){try{File.Delete(Path.Combine(root,"update-session.dpapi"));}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}}
    public void SaveKey(string secret)
    {
        Directory.CreateDirectory(root); var bytes=Encoding.UTF8.GetBytes(secret);
        try { Atomic(Path.Combine(root,"openrouter.dpapi"),Protect(bytes,false)); } finally { Array.Clear(bytes); }
    }
    // Only this application's own key file is read. Never enumerate Credential Manager or environment secrets.
    public string ReadKey()
    {
        var bytes=Protect(File.ReadAllBytes(Path.Combine(root,"openrouter.dpapi")),true);
        try { return Encoding.UTF8.GetString(bytes); } finally { Array.Clear(bytes); }
    }
    public void RemoveKey() { var path=Path.Combine(root,"openrouter.dpapi"); if(File.Exists(path)) File.Delete(path); }
    public static void Startup(bool enabled)
    {
        using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled) run.SetValue("StudyWhisper",'"'+Environment.ProcessPath+'"'+" --startup"); else run.DeleteValue("StudyWhisper",false);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob data,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob data,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr data);
    public static byte[] Protect(byte[] bytes,bool decrypt)
    {
        var input=new Blob {Size=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)}; Marshal.Copy(bytes,0,input.Data,bytes.Length);
        try
        {
            Blob output; var ok=decrypt?CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptProtectData(ref input,"StudyWhisper OpenRouter",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var result=new byte[output.Size]; Marshal.Copy(output.Data,result,0,output.Size); return result; }
            finally { for(int i=0;i<output.Size;i++) Marshal.WriteByte(output.Data,i,0); LocalFree(output.Data); }
        }
        finally { for(int i=0;i<input.Size;i++) Marshal.WriteByte(input.Data,i,0); Marshal.FreeHGlobal(input.Data); }
    }
}
