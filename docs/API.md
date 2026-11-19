# Contratos oficiais

Verificados em 03/10/2026 por documentação oficial e consultas públicas sem autenticação. Não houve chamada autenticada de validação ou inferência.

| Etapa | Endpoint | Contrato usado |
| --- | --- | --- |
| Chave | `GET /api/v1/key` | Bearer da chave digitada, resposta `data` objeto; sem management API |
| Catálogo | `GET /api/v1/models?output_modalities=...` | `transcription`, `decisions`, `text`; filtrar também `architecture.input_modalities` |
| STT | `POST /api/v1/audio/transcriptions` | JSON `model`, `input_audio.data` base64, `input_audio.format=wav`, `response_format=json`, `language=pt`; ler `text` |
| JEV | `POST /api/alpha/decisions` | `model`, `state.transcript`, `state.recent_context`, `questions.action` tipo `choice`, instruções e três critérios; ler `answers.action` |
| Resposta | `POST /api/v1/chat/completions` | Mensagens textuais system/user/assistant, `stream=false`, `max_tokens=1400`; ler `choices[0].message.content` |

Todos os endereços são fixos sob `https://openrouter.ai/`. O HTTP não segue redirects. A contabilidade compartilhada conta tentativas de inferência, inclusive falhas, e mantém cooldown de HTTP 429. Desde 0.1.12 não há teto local de quantidade de chamadas ou gasto. Respostas HTTP são limitadas a 8 MB; não são exibidos corpos de erro, chaves, labels de conta ou metadados de autenticação.

STT padrão `openai/whisper-large-v3-turbo` e JEV padrão `typesafe/jev-1.14` foram encontrados no catálogo público. O modelo de resposta deve ser escolhido pelo usuário. Modelos de chat com áudio não são apresentados como STT; `typesafe/jev-router` não é apresentado como classificador. O catálogo incluído é uma conveniência datada; atualização e seleção validam a modalidade, sem prometer saldo, acesso ou sucesso na conta.

As opções de STT em destaque foram conferidas no catálogo público em 19/11/2026. Cada taxa de erro indica o recorte usado na avaliação. O Scribe v2 Medical aparece primeiro e seu WER clínico foi medido em inglês, francês e alemão. A taxa dos Voxtral 3 vem do AA-WER, que identifica a revisão avaliada. [MedDictate, avaliado pela ElevenLabs](https://elevenlabs.io/pt/blog/scribe-v2-medical-generally-available) · [Voice Arena PT-BR](https://voicearena.com/stt-leaderboard/brazilian-portuguese) · [Artificial Analysis](https://artificialanalysis.ai/speech-to-text/non-streaming?error-rate=word-error-rate) · [Koedesk](https://koedesk.app/benchmark/) · [Vozbench PT-BR](https://vozbench.com/en/stt/best-portuguese-speech-to-text/)

JEV é um modelo textual de decisões, não chat. O app não tenta fazê-lo receber áudio ou gerar respostas livres. Suas probabilidades expressam a distribuição entre critérios; não comprovam precisão de classificação em português.

Com ZDR ativado, cada corpo inclui `provider: {zdr: true, data_collection: "deny"}`. A documentação canônica de STT suporta esses campos, embora o blog antigo tenha outras restrições. Preferências `order`, `only` e `ignore` não se aplicam a STT e não são enviadas. O schema atual de Decisions inclui esses campos em `ProviderPreferences`; ainda depende da rota JEV servir a política solicitada. Falta de rota compatível produz erro sem fallback que relaxe a política.

Fontes primárias:

- [STT e políticas de provedor](https://openrouter.ai/docs/guides/overview/multimodal/stt)
- [Catálogo e modalidades](https://openrouter.ai/docs/api/api-reference/models/list-all-models-and-their-properties)
- [JEV: conceitos e modelo](https://openrouter.ai/docs/guides/community/jev)
- [Decisions: schema de requisição e resposta](https://openrouter.ai/docs/api/api-reference/alphadecisions/submit-a-decisions-request)
- [Chat completions](https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion)
- [Validação da chave atual](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key)
- [ZDR](https://openrouter.ai/docs/guides/features/zdr)
- [DPAPI no Windows](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [Ícones canônicos Segoe MDL2](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font)

O app utiliza os glifos do Windows instalado, sem distribuir a fonte: Edit E70F, Cancel E711, Processing E9F5, ThoughtBubble EA91, Wait E823, Warning E7BA, Microphone E720 e Pause E769.


Desde 0.1.2, JEV responde somente a perguntas claras (inclusive contextuais e sem '?'), aguarda fala incompleta e ignora enunciados e comandos sem pergunta. Contadores leem usage.cost quando presente, sem consultas adicionais de faturamento; ausente ou parcial é mostrado como desconhecido. Consulte [Usage Accounting oficial](https://openrouter.ai/docs/cookbook/administration/usage-accounting).


Em 04/10/2026 foi conferido o contrato oficial de [Web Search Server Tool](https://openrouter.ai/docs/guides/features/server-tools/web-search). A implementação opcional, os limites e a retenção são descritos em [Conversa 0.1.7](CONVERSATION.md). Desde 0.1.7, a ferramenta Exa pode operar com ZDR na inferência, preservando `provider.zdr=true` e `data_collection=deny`. Retenção da Exa é independente; o bloqueio da 0.1.6 era uma escolha incorreta do app, removida. Não houve chamada real autenticada de busca.

Em 15/10/2026, o catálogo de decisões e o endpoint público confirmaram `typesafe/jev-1.14`, revisão `typesafe/jev-1.14-20261014`, com entrada textual e saída `decisions`. A versão local 0.1.9 mantém `POST /api/alpha/decisions` e o contrato `answers.action` do tipo `choice`, incluindo probabilidades e limiar 0,65. JEV 1.13 e o alias latest permanecem no catálogo para seleção manual. Falhas continuam interrompendo o fluxo, sem repetição automática, troca silenciosa de modelo ou relaxamento de ZDR/coleta.

## Transcrição contínua na versão 0.1.23

O modo Transcrição usa o endpoint de áudio com o modelo STT e a política de dados configurados. Parar conclui os blocos pendentes de STT. Somente o modo Perguntas chama o classificador e pode solicitar uma resposta ao chat.

## Contrato histórico de reescrita de gravações da versão 0.1.16

A finalização usa o mesmo `POST /api/v1/chat/completions`, modelo de resposta configurado e política de provedor, sem ferramentas de pesquisa. Para cada parte: `stream=false`, `max_tokens=12000`, `temperature=0`, `reasoning.effort=low`. A implementação exige conteúdo textual não vazio, ausência de recusa e `choices[0].finish_reason=stop`; outras finalizações não são apresentadas como texto final completo. O contrato de campos foi consultado em 04/11/2026 na [referência oficial de Chat Completions](https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion).

Esses critérios confirmam conclusão do protocolo e verificações literais específicas, não a exatidão ou completude semântica da reescrita. Nenhuma inferência autenticada foi realizada nesta revisão.
