using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace StudyWhisper.Core;

public sealed record TranscriptWord(string Word,double Start,double End,double? Confidence=null);
public sealed record RecordedTranscript(string Text,TranscriptWord[]? Words=null,double? Confidence=null);
public interface IRecordingApi
{
    Task<RecordedTranscript> TranscribeRecordingAsync(byte[] wav,CancellationToken token);
}
public sealed record RecordingProfile(int WindowSeconds=12,int OverlapSeconds=3,int PreviewSeconds=6)
{
    public void Validate(){if(WindowSeconds is < 3 or > 30||OverlapSeconds<0||OverlapSeconds>=WindowSeconds||PreviewSeconds<0||PreviewSeconds>=WindowSeconds)throw new ArgumentException("Janelas de gravação inválidas.");}
}
public sealed record RecordingRevision(long Sequence,string Text,bool Provisional,bool NeedsReview,int ContentStart=0,bool Superseded=false);

/// <summary>Reconciles only lexically matching words at the same received-audio time. No confidence threshold, fuzzy edit or global text deduplication.</summary>
public static class RecordingBoundary
{
    private sealed record Token(string Value,int Start,int End,double From,double To);
    private sealed record Anchor(int Old,int Current,int Count,double Error);
    private static readonly Regex Words=new(@"[+\-−±]?\p{N}+(?:[.,:/-]\p{N}+)*[%‰°]?|[\p{L}\p{M}\p{N}]+|[\p{S}%‰°\-]",RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static string Normalize(string value)=>value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    private static Token[]? Tokens(RecordedSegment segment)
    {
        if(segment.Words is not {Length:>0})return null;
        var text=segment.OriginalText;var matches=Words.Matches(text);if(matches.Count!=segment.Words.Length)return null;
        var result=new Token[matches.Count];double last=-1;
        for(var i=0;i<result.Length;i++)
        {
            var word=segment.Words[i];if(word is null||word.Word is null)return null;var normalized=Words.Matches(word.Word);
            if(normalized.Count!=1||Normalize(normalized[0].Value)!=Normalize(matches[i].Value)||!double.IsFinite(word.Start)||!double.IsFinite(word.End)||word.Start<last||word.Start<0||word.End<word.Start||word.End>segment.To-segment.From+.25)return null;
            last=word.Start;result[i]=new(Normalize(matches[i].Value),matches[i].Index,i+1<matches.Count?matches[i+1].Index:text.Length,segment.From+word.Start,segment.From+word.End);
        }
        return result;
    }
    public static int StableEnd(RecordedSegment segment,double audioTime)
    {
        var tokens=Tokens(segment);if(tokens is null)return segment.ContentStart;
        return Math.Max(segment.ContentStart,tokens.TakeWhile(t=>t.To<=audioTime).Select(t=>t.End).DefaultIfEmpty(segment.ContentStart).Max());
    }
    public static RecordingRevision[]? Reconcile(RecordedSegment previous,RecordedSegment current)
    {
        if(current.OriginalText.Length==0)return new[]{new RecordingRevision(previous.Sequence,previous.Text,false,previous.NeedsReview,previous.ContentStart)};
        if(current.From>=previous.To)return new[]{new RecordingRevision(previous.Sequence,previous.Text,false,previous.NeedsReview,previous.ContentStart)};
        // Extra acoustic coverage alone does not prove the preview's words survived recognition.
        if(Math.Abs(current.From-previous.From)<.001&&current.To>=previous.To)
        {
            var preview=Words.Matches(previous.OriginalText).Select(m=>Normalize(m.Value)).ToArray();
            var replacement=Words.Matches(current.OriginalText).Select(m=>Normalize(m.Value)).ToArray();
            if(previous.OriginalText.Length==0||preview.Length>0&&replacement.Take(preview.Length).SequenceEqual(preview))
                return new[]{new RecordingRevision(previous.Sequence,"",false,false,0,true)};
            return null;
        }
        var old=Tokens(previous);var next=Tokens(current);if(old is null||next is null)return null;
        if(!old.Any(t=>t.To>current.From&&t.From<previous.To)&&!next.Any(t=>t.To>current.From&&t.From<previous.To))return new[]{new RecordingRevision(previous.Sequence,previous.Text,false,previous.NeedsReview,previous.ContentStart)};
        bool Same(int a,int b)
        {
            var x=old[a];var y=next[b];var mx=(x.From+x.To)/2;var my=(y.From+y.To)/2;
            return x.Start>=previous.ContentStart&&x.Value==y.Value&&mx>=current.From-.1&&mx<=previous.To+.1&&my>=current.From-.1&&my<=previous.To+.1&&Math.Abs(mx-my)<=.35;
        }
        var anchors=new List<Anchor>();
        for(var a=0;a<old.Length;a++)for(var b=0;b<next.Length;b++)
        {
            if(!Same(a,b)||a>0&&b>0&&Same(a-1,b-1))continue;
            var count=0;double error=0;
            while(a+count<old.Length&&b+count<next.Length&&Same(a+count,b+count))
            {error+=Math.Abs((old[a+count].From+old[a+count].To-next[b+count].From-next[b+count].To)/2);count++;}
            if(count>=3&&error/count<=.25)anchors.Add(new(a,b,count,error/count));
            else if(count>0)
            {
                // A short overlap is usable only with nearly identical actual word intervals and a unique temporal match.
                var x=old[a];var y=next[b];
                if(Math.Abs(x.From-y.From)<=.08&&Math.Abs(x.To-y.To)<=.08&&Enumerable.Range(0,next.Length).Count(n=>Same(a,n))==1&&Enumerable.Range(0,old.Length).Count(n=>Same(n,b))==1)
                    anchors.Add(new(a,b,1,error/count));
            }
        }
        if(anchors.Count==0)return null;
        var best=anchors.OrderBy(a=>a.Error).ThenByDescending(a=>a.Count).ThenBy(a=>a.Old).First();
        if(anchors.Any(a=>a.Old-a.Current!=best.Old-best.Current&&a.Error<=best.Error+.1))return null;
        // Every discarded word needs an equivalent retained counterpart, not just a common anchor.
        // A changed negation, value or suffix therefore leaves both hypotheses visible for review.
        var offset=best.Old-best.Current;
        if(offset<0||next.Length<old.Length-offset)return null;
        for(var b=0;b<old.Length-offset;b++)if(!Same(offset+b,b))return null;
        var length=Math.Min(3,best.Count);var oldEnd=old[best.Old+length-1].End;var newStart=next[best.Current+length-1].End;
        if(oldEnd<previous.ContentStart)return null;
        return new[]{new RecordingRevision(previous.Sequence,previous.OriginalText[previous.ContentStart..oldEnd].Trim(),false,previous.NeedsReview,previous.ContentStart),new RecordingRevision(current.Sequence,current.OriginalText[newStart..].Trim(),true,false,newStart)};
    }
}
