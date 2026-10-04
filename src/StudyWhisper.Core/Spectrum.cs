namespace StudyWhisper.Core;

public readonly record struct AudioBands(double Low,double Mid,double High,double Intensity)
{public static AudioBands Silent=>new(0,0,0,0);}

/// <summary>1024 point radix-2 Hann FFT, at most ten analyses/second of captured PCM.
/// Absolute intensity prevents normalized near-silence from glowing. No semantic inference.</summary>
public sealed class Spectrum
{
    public const int Size=1024, Hop=1600;
    private readonly double[] real=new double[Size],imag=new double[Size],window=new double[Size];
    private readonly short[] samples=new short[Size];
    private int cursor,filled,since;
    public long Analyses {get;private set;}
    public Spectrum(){for(var i=0;i<Size;i++)window[i]=.5-.5*Math.Cos(2*Math.PI*i/(Size-1));}
    public AudioBands? Push(ReadOnlySpan<short> frame)
    {
        foreach(var x in frame){samples[cursor]=x;cursor=(cursor+1)%Size;filled=Math.Min(Size,filled+1);since++;}
        if(filled<Size||since<Hop)return null;since=0;Analyses++;
        double power=0;
        for(var i=0;i<Size;i++){var x=samples[(cursor+i)%Size]/32768.0;power+=x*x;real[i]=x*window[i];imag[i]=0;}
        for(int i=1,j=0;i<Size;i++){var bit=Size>>1;for(; (j&bit)!=0;bit>>=1)j^=bit;j^=bit;if(i<j){(real[i],real[j])=(real[j],real[i]);}}
        for(var length=2;length<=Size;length<<=1)
        {
            var angle=-2*Math.PI/length;var cr=Math.Cos(angle);var ci=Math.Sin(angle);
            for(var i=0;i<Size;i+=length){double wr=1,wi=0;for(var j=0;j<length/2;j++){var a=i+j;var b=a+length/2;var tr=wr*real[b]-wi*imag[b];var ti=wr*imag[b]+wi*real[b];real[b]=real[a]-tr;imag[b]=imag[a]-ti;real[a]+=tr;imag[a]+=ti;(wr,wi)=(wr*cr-wi*ci,wr*ci+wi*cr);}}
        }
        double low=0,mid=0,high=0;
        for(var k=1;k<Size/2;k++){var hz=(double)k*Vad.Rate/Size;var p=real[k]*real[k]+imag[k]*imag[k];if(hz<80)continue;if(hz<350)low+=p;else if(hz<2000)mid+=p;else high+=p;}
        var total=low+mid+high;var rms=Math.Sqrt(power/Size);
        var intensity=rms<.001?0:Math.Clamp((20*Math.Log10(rms)+60)/48,0,1);
        return total<1e-12||intensity==0?AudioBands.Silent:new(Math.Sqrt(low/total)*intensity,Math.Sqrt(mid/total)*intensity,Math.Sqrt(high/total)*intensity,intensity);
    }
    public void Reset(){Array.Clear(samples);Array.Clear(real);Array.Clear(imag);cursor=filled=since=0;}
}
