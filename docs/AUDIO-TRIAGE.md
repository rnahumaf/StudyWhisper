# Triagem local de áudio — 0.1.2

O caminho de produção é PCM16 mono 16 kHz → Silero VAD local → WAV limitado → `openai/whisper-large-v3-turbo` no OpenRouter → JEV textual → resposta somente a perguntas. Não há reconhecimento local, filtro semântico de áudio, regex de perguntas em produção ou economia por reduzir chamadas ao JEV.

## Modelo e licença

- Silero VAD **v6.2.3**, commit `5cd7945676eb32225748052e2e6a0580e4686a08`.
- Arquivo oficial `src/silero_vad/data/silero_vad_16k_op15.onnx`, **1.289.603 bytes**, MIT.
- SHA-256 `7ED98DDBAD84CCAC4CD0AEB3099049280713DF825C610A8ED34543318F1B2C49`.
- [Origem imutável do modelo](https://github.com/snakers4/silero-vad/blob/5cd7945676eb32225748052e2e6a0580e4686a08/src/silero_vad/data/silero_vad_16k_op15.onnx), [wrapper oficial](https://github.com/snakers4/silero-vad/blob/5cd7945676eb32225748052e2e6a0580e4686a08/src/silero_vad/utils_vad.py), [licença](https://github.com/snakers4/silero-vad/blob/5cd7945676eb32225748052e2e6a0580e4686a08/LICENSE).
- ONNX Runtime CPU **1.30.0**, [pacote oficial Microsoft](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime/1.30.0), MIT. Licenças e avisos de terceiros são distribuídos junto ao EXE e instalador.

O modelo está incorporado ao assembly, com verificação de hash antes de iniciar a captura. Estado recorrente `[2,1,128]`, entrada de 512 amostras e contexto de 64 seguem o contrato oficial. CPU somente, uma thread intra/inter-op, sessão reutilizada; não exige GPU/NPU. Falha no carregamento ou inferência pausa o monitoramento, sem voltar silenciosamente ao VAD de energia. Nenhum modelo é baixado durante o uso.

## Segmentação e limites

A captura mantém quadros de 20 ms; o filtro remonta blocos de 32 ms. Início exige dois blocos consecutivos acima da probabilidade configurada, padrão 0,50. O limiar para manter o segmento é 0,15 menor, com piso 0,15. Não há duração mínima de frase além desses 64 ms de evidência: frases curtas como “por quê?” não são descartadas por tamanho.

O pré-roll guarda até 320 ms, incluindo os blocos de início. O fim aguarda 800 ms abaixo do limiar de manutenção; o WAV conserva 256 ms de margem final e remove somente o restante desse silêncio terminal. Pausas menores permanecem dentro da fala. A margem não garante preservação de toda fala fraca: se o modelo demorar mais de 320 ms para detectar ou deixar de reconhecer o fim por mais de 256 ms, fonemas podem se perder. Aumentar a probabilidade reduz sensibilidade.

Segmentos respeitam o máximo configurado de 3–30 s, padrão 15 s, arredondado para blocos de 32 ms. No limite, o WAV é emitido com 256 ms de sobreposição no próximo; o estado neural continua, evitando reinício artificial no meio da fala. Isso preserva amostras na fronteira, mas não garante transcrição perfeita ou ausência de palavras repetidas. JEV pode aguardar continuação de perguntas incompletas por 12 s. Perguntas longas podem ser fragmentadas; o pipeline continua com uma inferência por vez e fila de até **dois WAVs pendentes**, cada um de no máximo 30 s. A fila expira em **30 s** (verificação a cada segundo e antes de consumir). Duas perguntas consecutivas são processadas em ordem, usando contexto atualizado. Se lotar, a fala nova substitui a pendente mais antiga, que é zerada; a fala em processamento não é interrompida. Pausar, limpar, alterar configurações ou sair cancela e zera a fila imediatamente; retomar nunca envia material anterior à pausa. Excesso, expiração e cancelamento têm contadores e não são atribuídos à economia do VAD. Chamadas da fila obedecem às mesmas quotas, timeout e política de dados. Falas mais rápidas que a API por tempo prolongado, quota esgotada ou espera acima de 30 s ainda podem perder pedidos; as mensagens informam esses limites.

Pré-roll máximo 5.120 amostras; segmento máximo 480.000; remanescente de montagem menor que 512. A fila ocupa no máximo 1.920.088 bytes de WAV (dois segmentos de 30 s), além do WAV em processamento; não há backlog ilimitado. Buffers PCM/WAV são zerados ao consumir, limpar, pausar ou descartar. Só agregados ficam na RAM; não há áudio pessoal, transcrições, payloads ou identificadores de chamada em arquivos de métricas.

## JEV e perguntas

O contrato foi restringido a perguntas claras de estudo, com ou sem “?”, incluindo perguntas contextuais quando há contexto. Fala incompleta aguarda; enunciados, comentários, interjeições e imperativos sem pergunta são ignorados. O JEV recebe todo texto transcrito não vazio, sem filtro regex antes dele. Probabilidade de responder mínima 0,65 e validação estrutural continuam. Os testes verificam o contrato e roteamento com mocks; não medem acurácia semântica do JEV real.

TV, outras pessoas, músicas com voz e perguntas dirigidas a outro interlocutor podem passar. O Silero detecta fala, não intenção ou destinatário. O usuário pode pausar pela bandeja em ambiente ruidoso. Pressionar para falar não foi acrescentado nesta versão; a escuta natural permanece padrão.

## Contadores e custo

Configurações mostra os agregados desta execução, preservados entre pausas e alterações de configuração:

- Monitorado: duração dos quadros PCM recebidos enquanto ativado; fala detectada: blocos com probabilidade acima do limiar de manutenção, não verdade anotada.
- Áudio enviado ao STT: tamanho PCM dos WAVs no início das tentativas HTTP, incluindo pré-roll, margem final e sobreposição. Uma falha/cancelamento não garante que o provedor recebeu ou deixou de cobrar.
- Trechos sem fala descartados: episódios acústicos com RMS ≥0,005 sem atingir início neural, encerrados após 800 ms de quietude ou 30 s. Silêncio puro não inventa “segmentos descartados”. RMS é usado somente para essa contagem, nunca para autorizar STT.
- Fila atual, excesso, expiração, cancelamento de pendentes, buffer parcial cancelado e cortes no limite são contados separadamente. Chamadas distinguem STT, JEV e resposta; consultas de chave/catálogo não entram.
- `usage.cost`, quando numérico e não negativo na resposta, é somado. Campo ausente/falha/cancelamento deixa cobertura desconhecida; zero explícito conta como informado. Valores parciais mostram `x/N chamadas` e “restante desconhecido”. Não existe teto em dólares inferido de saldo ou de custo ausente. [Usage Accounting oficial](https://openrouter.ai/docs/cookbook/administration/usage-accounting).

A referência histórica de STT **US$ 0,012–0,040 por hora efetivamente enviada** continua uma estimativa de rotas, separada do custo informado, sem garantir preço na conta, cobrança mínima ou custo por hora de monitoramento. JEV e resposta são adicionais. Não foi assumido mínimo de 10 s/request da Groq fora do OpenRouter.

## Verificação offline e limites

As fixtures pt-BR foram geradas **sem reprodução** pelo TTS local Microsoft Daniel, com textos inventados. `tests/fixtures/provenance.json` registra texto, gerador, formato e hash. `scripts/Generate-SpeechFixture.ps1` permite regenerar quando essa voz está disponível. São artefatos sintéticos de teste, não áudio humano ou coleta pelo app; não validam sotaques, conversas, ruído real ou uso clínico.

`scripts/Test.ps1 -Ui` roda testes de margem, histerese, frase curta, limite/sobreposição, reset, cancelamento, estado antigo, mock HTTP/custo, JEV e WPF. A auditoria compara a implementação de energia antiga com o filtro neural nos mesmos sinais e produz `artifacts/audio-triage-audit.json`. `--audio-smoke --output <pasta>` valida o modelo incorporado e a DLL nativa no EXE publicado, sem Controller, chave, HTTP ou dispositivo.

| Sinal offline | VAD antigo: WAV exportado | Silero: WAV exportado |
| --- | ---: | ---: |
| Silêncio, 60 s | 0 s | 0 s |
| Tom 220 Hz, 60 s | 60 s / 4 segmentos | **0,704 s / 1 segmento** |
| Tom 1.000 Hz, 60 s | 60 s / 4 segmentos | 0 s |
| Ruído branco, 30 s | 30 s / 2 segmentos | 0 s |
| Clicks, 30 s | 0 s | 0 s |
| Pergunta pt-BR sintetizada | 3,82 s | 3,616 s |
| “Por quê?” sintetizado | 1,30 s | 1,056 s |
| “Por quê?” com ganho 0,1 | 0 s (perdido) | 1,088 s |
| Comentário pt-BR sintetizado | 4,62 s | 4,000 s / 2 segmentos |

No tom 220 Hz, a redução de duração exportada foi **98,83%**, mantendo um falso positivo inicial. Não é garantia de zero chamadas para ruído. As três fixtures de fala e a variante de ganho baixo tiveram **zero amostras com amplitude absoluta >16 fora das margens exportadas**. A fala do comentário foi preservada: quem deve ignorá-lo é o JEV depois de STT. A auditoria roda diretamente o filtro, sem fila nem quotas; a redução acima não resulta dessas perdas.

Computador de referência: Intel Core 7 240H, 16 processadores lógicos, Windows build 26200, processo x64. Nas execuções offline, blocos de 32 ms levaram aproximadamente **0,10–0,14 ms em média**, p95 **0,10–0,22 ms**; os valores completos e CPU agregada por bloco estão no JSON. O custo de CPU do processo inclui enquadramento, VAD antigo para comparação e ONNX; o relógio de CPU tem granularidade, portanto trechos curtos podem aparecer com zero CPU. Os dez primeiros blocos são excluídos da estatística de tempo decorrido. Não é benchmark de laptop fraco ou medição de consumo de bateria; testar nesse hardware segue pendente. A consulta CIM foi negada pelo sandbox; o nome da CPU foi obtido por leitura de metadado público do registro.

Não houve microfone pessoal, chamadas autenticadas, medição de custo real, instalação ou reinício do app em uso. Compatibilidade nativa foi verificada no PC de referência. O [ONNX Runtime exige Visual C++ 2019 runtime ou compatível mais recente no Windows](https://onnxruntime.ai/docs/install/); o pacote não instala esse pré-requisito. CPU antiga, PC sem esse runtime, ambiente ruidoso e sotaques distintos ainda precisam de validação. O primeiro restore do SDK exibiu a criação automática de certificado de desenvolvimento ASP.NET; nenhum certificado foi lido/usado e os scripts agora desabilitam essa geração.
