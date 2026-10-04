# Contratos oficiais

Verificados em 03/10/2026 por documentação oficial e consultas públicas sem autenticação. Não houve chamada autenticada de validação ou inferência.

| Etapa | Endpoint | Contrato usado |
| --- | --- | --- |
| Chave | `GET /api/v1/key` | Bearer da chave digitada, resposta `data` objeto; sem management API |
| Catálogo | `GET /api/v1/models?output_modalities=...` | `transcription`, `decisions`, `text`; filtrar também `architecture.input_modalities` |
| STT | `POST /api/v1/audio/transcriptions` | JSON `model`, `input_audio.data` base64, `input_audio.format=wav`, `response_format=json`, `language=pt`; ler `text` |
| JEV | `POST /api/alpha/decisions` | `model`, `state.transcript`, `state.recent_context`, `questions.action` tipo `choice`, instruções e três critérios; ler `answers.action` |
| Resposta | `POST /api/v1/chat/completions` | Mensagens textuais system/user/assistant, `stream=false`, `max_tokens=1400`; ler `choices[0].message.content` |

Todos os endereços são fixos sob `https://openrouter.ai/`. O HTTP não segue redirects. O orçamento compartilhado conta inferências, inclusive falhas, e mantém cooldown de HTTP 429. Respostas HTTP são limitadas a 8 MB; não são exibidos corpos de erro, chaves, labels de conta ou metadados de autenticação.

STT padrão `openai/whisper-large-v3-turbo` e JEV padrão `typesafe/jev-1.13` foram encontrados no catálogo público. O modelo de resposta deve ser escolhido pelo usuário. Modelos de chat com áudio não são apresentados como STT; `typesafe/jev-router` não é apresentado como classificador. O catálogo incluído é uma conveniência datada; atualização e seleção validam a modalidade, sem prometer saldo, acesso ou sucesso na conta.

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


Em 04/10/2026 foi conferido o contrato oficial de [Web Search Server Tool](https://openrouter.ai/docs/guides/features/server-tools/web-search). A implementação opcional, os limites e a retenção são descritos em [Conversa 0.1.6](CONVERSATION.md). A ferramenta de busca fica bloqueada sob ZDR. Não houve chamada real autenticada de busca.
