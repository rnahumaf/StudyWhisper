using System.Buffers.Binary;
using StudyWhisper.Core;
namespace StudyWhisper.TestAudio;
public static class AudioFixtures
{
    public static short[] Load(string path)
    {
        var b=File.ReadAllBytes(path);
        if(!b.AsSpan(0,4).SequenceEqual("RIFF"u8)||!b.AsSpan(8,4).SequenceEqual("WAVE"u8))throw new InvalidDataException("Not WAV");
        var valid=false;
        for(var i=12;i+8<=b.Length;)
        {
            var size=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i+4));if(size<0||i+8+size>b.Length)throw new InvalidDataException("Invalid WAV chunk");
            if(b.AsSpan(i,4).SequenceEqual("fmt "u8))valid=size>=16&&BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i+8))==1&&BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i+10))==1&&BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i+12))==16000&&BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i+22))==16;
            if(b.AsSpan(i,4).SequenceEqual("data"u8))
            {
                if(!valid||size%2!=0)throw new InvalidDataException("Expected PCM16 mono 16 kHz");var result=new short[size/2];
                for(var n=0;n<result.Length;n++)result[n]=BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i+8+2*n));return result;
            }
            i+=8+size+(size%2);
        }
        throw new InvalidDataException("Missing WAV data");
    }
    public static IEnumerable<short[]> Frames(short[] pcm,int leading=16000,int trailing=16000,double gain=1)
    {
        var count=(leading+pcm.Length+trailing+Vad.FrameSamples-1)/Vad.FrameSamples;
        for(var f=0;f<count;f++)
        {
            var frame=new short[Vad.FrameSamples];
            for(var n=0;n<frame.Length;n++){var at=f*frame.Length+n-leading;if(at>=0&&at<pcm.Length)frame[n]=(short)Math.Clamp(pcm[at]*gain,short.MinValue,short.MaxValue);}
            yield return frame;
        }
    }
}
