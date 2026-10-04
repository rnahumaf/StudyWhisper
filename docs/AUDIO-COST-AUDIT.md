# Escuta, envio e custo — auditoria de 03/10/2026

A auditoria usou o código do repositório e os padrões de `Settings.cs`. Não leu a chave, a configuração pessoal ou conversas da sessão do usuário. O filtro de captura não foi alterado nesta atualização.

## Quando há envio

Com monitoramento ativado, WinMM captura PCM16 mono a 16 kHz em quatro buffers de 20 ms, processados localmente. `Vad.Push` calcula RMS normalizado; o limiar padrão é 0,018, cerca de −34,9 dBFS. Três quadros consecutivos acima dele iniciam o segmento. É preciso acumular pelo menos dez quadros acima do limiar (200 ms; não necessariamente consecutivos após o início). O segmento termina após 35 quadros abaixo do limiar (700 ms) ou ao alcançar 15 segundos. O máximo configurável é 30 segundos.

O pré-roll conserva até dez quadros (200 ms), incluindo os quadros que iniciam a detecção. O WAV enviado também contém o silêncio final. Há, portanto, áudio que não é fala dentro de um segmento aceito. Silêncio contínuo e episódios breves abaixo do mínimo não produzem segmentos.

`Controller.OnFrame` passa o segmento a `Pipeline.SubmitAsync`. Se o pipeline está pausado ou ocupado, o WAV é descartado e zerado. Se está livre, o segmento inteiro segue para STT. Só depois da transcrição vem a classificação JEV. Texto vazio evita JEV; ignorar, aguardar ou probabilidade insuficiente evitam chat. Uma pergunta aceita usa STT + JEV + chat. Um comentário transcrito e ignorado normalmente já usou STT + JEV.

Não existe classificador semântico local, reconhecimento de falante ou filtro de palavra de ativação. Tom contínuo, TV, outras pessoas e ruído sustentado podem passar pelo filtro de energia. A decisão correta do JEV poupa a resposta, mas chega tarde para evitar o STT daquela fala.

Ao desativar o monitoramento pelo app, `Pause` desabilita o pipeline, fecha o dispositivo de gravação, cancela as chamadas e zera o VAD. Quadros que chegarem durante essa transição são recusados por `Enabled`. Não há captura nova depois do fechamento. Cancelamento de requisição já enviada não garante estorno. O app não consulta um estado global de mute do Windows; esse controle externo depende do dispositivo e não foi testado nesta auditoria.

## Medição sem microfone ou rede

`tests/StudyWhisper.AudioAudit` executa o VAD real com PCM sintético. Os valores abaixo são segmentos exportados; não houve envio à API.

| Fixture | Segmentos aceitos | Áudio exportado |
| --- | ---: | ---: |
| 60 s de silêncio | 0 | 0 s |
| Tom de 100 ms, seguido de silêncio | 0 | 0 s |
| Tom de 1 s, seguido de silêncio | 1 | 1,70 s |
| 200 ms de silêncio + tom de 1 s + silêncio | 1 | 1,84 s |
| Tom contínuo de 60 s | 4 | 60 s, em blocos de 15 s |

O tom passar confirma que o filtro atual não comprova a presença de fala. Não representa reconhecimento de intenção ou teste com TV real. Resultados reproduzíveis estão em `artifacts/audio-audit.json`.

## Limites e custo

Padrões: 12 chamadas de inferência por minuto e 300 por execução do app, compartilhadas entre STT, JEV e resposta. Falhas consomem o orçamento; HTTP 429 cria cooldown. Não há repetição automática. Limites não param a captura local; impedem novas inferências. Consultar catálogo e validar uma chave por ação explícita não consome esse orçamento. Ele é um limite de quantidade de chamadas, não um orçamento em dólares.

Com todas as chamadas bem-sucedidas, 300 permite até 100 perguntas com resposta ou 150 comentários com transcrição não vazia e classificação, antes de contar qualquer outro episódio. Transcrição vazia usa só uma chamada. Latência, duração dos segmentos e descarte durante processamento também reduzem o número de envios. Reiniciar o app abre outra sessão e reinicia o orçamento.

Para `openai/whisper-large-v3-turbo`, o endpoint público consultado em 03/10/2026 informa US$ 0,00000333/s na DeepInfra e US$ 0,0000111111111111/s na Groq. Esses valores correspondem aproximadamente a US$ 0,012–0,040 por **hora de áudio enviada**, não por hora com o app aberto. Dez minutos enviados correspondem a cerca de US$ 0,002–0,0067, apenas STT. A hipótese é cobrança proporcional aos segundos, sem presumir mínimos por solicitação, taxas adicionais, descontos ou provedor efetivamente escolhido. STT não permite fixar `order/only/ignore` pela política de chat; o app não promete a rota mais barata.

JEV e o modelo de resposta acrescentam custo por tokens. A memória envia até quatro turnos anteriores. Não é possível estimar o custo total da sessão pessoal sem conhecer os modelos, a rota, duração enviada e uso retornado, dados que não foram acessados nesta auditoria.

Fontes: [modelo e preço por segundo](https://openrouter.ai/openai/whisper-large-v3-turbo), [endpoints e preços públicos](https://openrouter.ai/api/v1/models/openai/whisper-large-v3-turbo/endpoints), [contrato STT](https://openrouter.ai/docs/guides/overview/multimodal/stt).


Esta auditoria descreve o VAD por energia da versão 0.1.1. A versão 0.1.2 usa Silero em produção; consulte [a comparação neural](AUDIO-TRIAGE.md). O runner atual gera audio-triage-audit.json; a evidência histórica permanece em artifacts/0.1.1-audio-audit.json.
