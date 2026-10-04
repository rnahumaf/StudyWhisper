using Microsoft.ML.OnnxRuntime;
using System.Security.Cryptography;
namespace StudyWhisper.Core;

public interface ISpeechProbability : IDisposable
{
    float Predict(ReadOnlySpan<short> pcm);
    void Reset();
}

/// <summary>Silero 6.2.3, official 16 kHz/opset 15 model; CPU only, one inference thread.</summary>
public sealed class Silero : ISpeechProbability
{
    public const int Samples = 512;
    public const string ModelHash = "7ED98DDBAD84CCAC4CD0AEB3099049280713DF825C610A8ED34543318F1B2C49";
    private readonly InferenceSession session;
    private readonly RunOptions run = new();
    private readonly float[] input = new float[576], state = new float[256];
    private readonly OrtValue inputTensor, stateTensor, rateTensor;
    private readonly OrtValue[] inputs;
    private bool disposed;
    public Silero()
    {
        using var stream = typeof(Silero).Assembly.GetManifestResourceStream("StudyWhisper.Core.Models.silero-vad-6.2.3-16k.onnx")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); var model = bytes.ToArray();
        if(Convert.ToHexString(SHA256.HashData(model)) != ModelHash) throw new InvalidDataException("Modelo local Silero inválido.");
        using var options = new SessionOptions { InterOpNumThreads=1, IntraOpNumThreads=1, ExecutionMode=ExecutionMode.ORT_SEQUENTIAL };
        session = new InferenceSession(model, options);
        inputTensor = OrtValue.CreateTensorValueFromMemory(input, new long[]{1,576});
        stateTensor = OrtValue.CreateTensorValueFromMemory(state, new long[]{2,1,128});
        rateTensor = OrtValue.CreateTensorValueFromMemory(new long[]{16000},Array.Empty<long>());
        inputs = new[]{inputTensor,stateTensor,rateTensor};
        if(!session.InputNames.SequenceEqual(new[]{"input","state","sr"}) || !session.OutputNames.SequenceEqual(new[]{"output","stateN"}))
            throw new InvalidDataException("Contrato do modelo Silero inesperado: "+string.Join(",",session.InputNames)+" / "+string.Join(",",session.OutputNames));
    }
    public float Predict(ReadOnlySpan<short> pcm)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(pcm.Length!=Samples) throw new ArgumentException("Silero exige 512 amostras (32 ms).");
        for(var i=0;i<Samples;i++) input[64+i]=pcm[i]/32768f;
        using var outputs = session.Run(run,session.InputNames,inputs,session.OutputNames);
        var probability = outputs[0].GetTensorDataAsSpan<float>()[0];
        outputs[1].GetTensorDataAsSpan<float>().CopyTo(state);
        input.AsSpan(512,64).CopyTo(input);
        if(!float.IsFinite(probability) || probability is <0 or >1) throw new InvalidDataException("Probabilidade Silero inválida.");
        return probability;
    }
    public void Reset() { Array.Clear(input);Array.Clear(state); }
    public void Dispose() { if(disposed)return;disposed=true;Reset();inputTensor.Dispose();stateTensor.Dispose();rateTensor.Dispose();run.Dispose();session.Dispose(); }
}
