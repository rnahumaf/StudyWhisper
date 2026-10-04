namespace StudyWhisper.Core;

// These messages contain neither PCM, transcripts, answers nor exception text.
public enum FlowKind { Enabled, Cancelled, Candidate, SpeechDetected, LocalApproved, LocalRejected, SttStarted, TranscriptReady, EmptyTranscript, JevStarted, JevIgnored, JevWaiting, JevApproved, AnswerStarted, ResponseReady, Throttled, Error, Queued, Overflow, Expired, Dequeued }
public record FlowNotice(FlowKind Kind,int Epoch,long Item=0,int Queued=0,bool FilterVerified=false);
public record FlowEvent(FlowKind Kind,long Generation,long Sequence,long Item=0,int Queued=0,bool FilterVerified=false);
public enum StepMark { Pending, Active, Approved, Rejected, Waiting, Error }
public sealed class FlowState
{
    public long Generation {get;private set;}=-1;
    public long Sequence {get;private set;}=-1;
    public long Item {get;private set;}
    public bool Enabled {get;private set;}
    public bool RemoteBusy {get;private set;}
    public bool Expanded {get;private set;}
    public int Queued {get;private set;}
    public StepMark Filter {get;private set;}
    public StepMark Whisper {get;private set;}
    public StepMark Jev {get;private set;}
    public StepMark Cloud {get;private set;}
    public string Reason {get;private set;}="Monitoramento pausado";
    public string Incoming {get;private set;}="";
    public FlowKind Last {get;private set;}=FlowKind.Cancelled;
    public bool Apply(FlowEvent e)
    {
        if(e.Generation<Generation || e.Generation==Generation&&e.Sequence<=Sequence)return false;
        if(e.Generation>Generation)Reset();
        Generation=e.Generation;Sequence=e.Sequence;Last=e.Kind;Queued=Math.Clamp(e.Queued,0,Pipeline.QueueCapacity);
        if(e.Kind==FlowKind.Cancelled){Reset();Reason="Cancelado · áudio temporário apagado";return true;}
        if(e.Kind==FlowKind.Enabled){Reset();Enabled=true;Reason="Monitoramento ativado";return true;}
        if(!Enabled)return false;
        if(e.Kind is FlowKind.Candidate or FlowKind.SpeechDetected or FlowKind.LocalApproved or FlowKind.LocalRejected)
        {
            Incoming=e.Kind switch {FlowKind.Candidate=>"Filtro avaliando",FlowKind.SpeechDetected=>"Fala detectada; aguardando pausa",FlowKind.LocalApproved=>"Trecho aprovado pelo filtro",_=>"Filtro: trecho sem fala"};
            if(!RemoteBusy){Expanded=true;Whisper=Jev=Cloud=StepMark.Pending;Filter=e.Kind==FlowKind.LocalApproved?StepMark.Approved:e.Kind==FlowKind.LocalRejected?StepMark.Rejected:StepMark.Active;Reason=Incoming;}
            return true;
        }
        if(e.Kind is FlowKind.Queued or FlowKind.Overflow or FlowKind.Expired or FlowKind.Dequeued)
        {
            Incoming=e.Kind switch {FlowKind.Overflow=>"Fila: pendente mais antiga descartada",FlowKind.Expired=>"Fila: pendente expirou (30 s)",FlowKind.Queued=>"Nova fala na fila",_=>"Próxima fala em processamento"};return true;
        }
        if(e.Kind==FlowKind.Throttled){RemoteBusy=false;Expanded=true;Reason="Limite de chamadas; aguarde o tempo indicado";return true;}
        if(e.Kind==FlowKind.SttStarted)
        {Item=e.Item;RemoteBusy=true;Expanded=true;Filter=e.FilterVerified?StepMark.Approved:StepMark.Pending;Whisper=StepMark.Active;Jev=Cloud=StepMark.Pending;Reason="Whisper · transcrevendo";return true;}
        if(e.Item!=Item)return false;
        switch(e.Kind)
        {
            case FlowKind.TranscriptReady: Whisper=StepMark.Approved;Reason="Transcrição recebida";break;
            case FlowKind.EmptyTranscript: Whisper=StepMark.Rejected;RemoteBusy=false;Reason="Whisper · sem fala reconhecida";break;
            case FlowKind.JevStarted: Jev=StepMark.Active;Reason="JEV · classificando texto";break;
            case FlowKind.JevIgnored: Jev=StepMark.Rejected;RemoteBusy=false;Reason="JEV · comentário ou interjeição";break;
            case FlowKind.JevWaiting: Jev=StepMark.Waiting;RemoteBusy=false;Reason="JEV · aguardando continuação";break;
            case FlowKind.JevApproved: Jev=StepMark.Approved;Reason="JEV · responder aprovado";break;
            case FlowKind.AnswerStarted: Cloud=StepMark.Active;Reason="Modelo de resposta · processando";break;
            case FlowKind.ResponseReady: Cloud=StepMark.Approved;RemoteBusy=false;Reason="Resposta recebida";break;
            case FlowKind.Throttled:
                RemoteBusy=false;Reason="Limite de chamadas; aguarde o tempo indicado";break;
            case FlowKind.Error:
                if(Cloud==StepMark.Active)Cloud=StepMark.Error;else if(Jev==StepMark.Active)Jev=StepMark.Error;else Whisper=StepMark.Error;
                RemoteBusy=false;Reason="Falha · consulte o aviso do indicador";break;
        }
        return true;
    }
    public void Collapse(){if(!RemoteBusy){Expanded=false;Incoming="";}}
    private void Reset(){Enabled=false;RemoteBusy=Expanded=false;Queued=0;Item=0;Filter=Whisper=Jev=Cloud=StepMark.Pending;Incoming="";}
}
