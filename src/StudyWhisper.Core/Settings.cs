namespace StudyWhisper.Core;

public sealed record Settings
{
    public string SttModel { get; init; } = "openai/whisper-large-v3-turbo";
    public string JevModel { get; init; } = "typesafe/jev-1.13";
    public string AnswerModel { get; init; } = "";
    public bool Memory { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool RequireZdr { get; init; }
    public bool EnableWebSearch { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public int CloseSeconds { get; init; } = 25;
    public int CallsPerMinute { get; init; } = 12;
    public int SessionCallLimit { get; init; } = 300;
    public double VadThreshold { get; init; } = 0.018; // Legacy settings compatibility/demo only; production uses SpeechProbability.
    public double SpeechProbability { get; init; } = 0.5;
    public int MaxAudioSeconds { get; init; } = 15;
    public void Validate()
    {
        if (CloseSeconds is < 5 or > 300 || CallsPerMinute is < 3 or > 60 ||
            SessionCallLimit is < 3 or > 3000 || MaxAudioSeconds is < 3 or > 30 ||
            !double.IsFinite(VadThreshold) || VadThreshold is < 0.002 or > 0.2 ||
            !double.IsFinite(SpeechProbability) || SpeechProbability is < 0.3 or > 0.8)
            throw new ArgumentException("Configuração fora dos limites permitidos.");
    }
}

public record Model(string Id, string Name, string[] Input, string[] Output)
{
    public override string ToString() => Id;
    public bool IsStt => Input.Contains("audio") && Output.Contains("transcription");
    public bool IsJev => Input.Contains("text") && Output.Contains("decisions") && (Id.StartsWith("typesafe/jev-") || Id == "~typesafe/jev-latest");
    public bool IsChat => Input.Contains("text") && Output.Contains("text");
}

public enum Decision { Respond, Wait, Ignore }
public record Judgment(Decision Action, double Confidence, double RespondProbability);
public record Citation(string Title, string Url);
public record StudyAnswer(string Content)
{
    public IReadOnlyList<Citation> Sources { get; init; } = Array.Empty<Citation>();
    public int? SearchRequests { get; init; }
}
public record Turn(string Question, string Answer, DateTimeOffset At)
{
    public IReadOnlyList<Citation> Sources { get; init; } = Array.Empty<Citation>();
    public int? SearchRequests { get; init; }
}
public enum Phase { Paused, Listening, Transcribing, Classifying, Thinking, Discarded, Waiting, Throttled, Error }
public record Status(Phase Phase, string Message);
public interface IStudyApi
{
    void EnsureCanStart() {}
    Task<string> TranscribeAsync(byte[] wav, CancellationToken token);
    Task<Judgment> ClassifyAsync(string transcript, string context, CancellationToken token);
    Task<StudyAnswer> AnswerAsync(string transcript, IReadOnlyList<Turn> memory, CancellationToken token);
}
