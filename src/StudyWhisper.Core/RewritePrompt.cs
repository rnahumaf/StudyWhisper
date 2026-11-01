using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace StudyWhisper.Core;

/// <summary>Generated, hash-verified snapshot of the canonical LMDocs composition. No runtime dependency on a sibling checkout.</summary>
public static class RewritePrompt
{
    private sealed record Part(string Id,string Body,string BodySHA256);
    private sealed record Bundle(string InstructionSHA256,Part[] Parts);
    private static readonly Lazy<string> instruction=new(Load);
    public static string Instructions=>instruction.Value;
    private static string Load()
    {
        using var stream=typeof(RewritePrompt).Assembly.GetManifestResourceStream("StudyWhisper.Core.Prompts.lmdocs-rewrite.json")??throw new InvalidDataException("Instrução de revisão indisponível.");
        var bundle=JsonSerializer.Deserialize<Bundle>(stream)??throw new InvalidDataException("Instrução de revisão inválida.");
        var ids=new[]{"global_natural_writing","rewrite_base","rewrite_mode_proofread","rewrite_style_default","rewrite_ai_patterns_skill"};
        if(!bundle.Parts.Select(p=>p.Id).SequenceEqual(ids)||bundle.Parts.Any(p=>Hash(p.Body)!=p.BodySHA256))throw new InvalidDataException("A instrução canônica de revisão não passou na verificação.");
        var text=string.Join("\n\n",bundle.Parts.Select(p=>p.Body));if(Hash(text)!=bundle.InstructionSHA256)throw new InvalidDataException("A composição de revisão foi alterada.");return text;
    }
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
