namespace StudyWhisper.Core;

public record SessionSnapshot(double MonitoredSeconds,double DetectedSpeechSeconds,double SttSeconds,
    int LocalRejected,int QueueOverflow,int BufferedDiscarded,int Boundaries,int Calls,int SttCalls,int JevCalls,int AnswerCalls,int CostReports,decimal ReportedCost,int QueueDepth,int QueuedTotal,int QueueExpired,int QueuedCancelled)
{
    public string CostText => CostReports==0?"Custo real: desconhecido (API não informou).":
        $"Custo informado: US$ {ReportedCost:0.000000} ({CostReports}/{Calls} chamadas; "+(CostReports==Calls?"cobertura completa).":"restante desconhecido).");
    public string Summary => $"Monitorado: {MonitoredSeconds:0.0} s · Fala detectada: {DetectedSpeechSeconds:0.0} s\n"+
        $"Áudio enviado ao STT: {SttSeconds:0.0} s ({SttCalls} chamadas)\n"+
        $"Trechos sem fala descartados: {LocalRejected} · Buffer cancelado: {BufferedDiscarded}\n"+
        $"Fila: {QueueDepth}/2 · Excesso: {QueueOverflow} · Expirados: {QueueExpired} · Cancelados: {QueuedCancelled}\n"+
        $"Chamadas: {Calls} (STT {SttCalls}, JEV {JevCalls}, resposta {AnswerCalls}) · Cortes com sobreposição: {Boundaries}\n"+CostText;
}
/// <summary>RAM-only aggregate counters; no audio, transcript, key, or request identifiers.</summary>
public sealed class SessionCounters
{
    private readonly object sync=new();
    private long monitored,speech;private double sttSeconds;
    private int localRejected,overflow,buffered,boundaries,calls,stt,jev,answer,costReports,queueDepth,queuedTotal,queueExpired,queuedCancelled;
    private decimal cost;
    public void Monitor(int samples){lock(sync)monitored+=samples;}
    public void Speech(int samples){lock(sync)speech+=samples;}
    public void RejectLocal(){lock(sync)localRejected++;}
    public void OverflowQueued(){lock(sync)overflow++;}
    public void ExpireQueued(){lock(sync)queueExpired++;}
    public void CancelQueued(){lock(sync)queuedCancelled++;}
    public void Queue(int depth,bool added=false){lock(sync){queueDepth=depth;if(added)queuedTotal++;}}
    public void DiscardBuffered(){lock(sync)buffered++;}
    public void Boundary(){lock(sync)boundaries++;}
    public void Call(string path,double seconds=0)
    {
        lock(sync){calls++;if(path.EndsWith("audio/transcriptions")){stt++;sttSeconds+=seconds;}
            else if(path.EndsWith("decisions"))jev++;else if(path.EndsWith("chat/completions"))answer++;}
    }
    public void Cost(decimal amount){if(amount<0)return;lock(sync){cost+=amount;costReports++;}}
    public SessionSnapshot Snapshot(){lock(sync)return new(monitored/(double)Vad.Rate,speech/(double)Vad.Rate,sttSeconds,localRejected,overflow,buffered,boundaries,calls,stt,jev,answer,costReports,cost,queueDepth,queuedTotal,queueExpired,queuedCancelled);}
}
