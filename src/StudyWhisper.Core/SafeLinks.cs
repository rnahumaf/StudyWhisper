namespace StudyWhisper.Core;

public static class SafeLinks
{
    public static bool TryParse(string? value,out Uri uri)
    {
        uri=null!;
        return value is {Length: >0 and <=2048} && Uri.TryCreate(value,UriKind.Absolute,out uri!) &&
            uri.Scheme is "https" or "http" && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    }
}
