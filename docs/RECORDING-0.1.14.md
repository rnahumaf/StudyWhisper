# Preparação local 0.1.14

Esta versão integra revisão de bordas STT, painel ancorado, controles diretos e Rewrite. Foi preparada em cópia isolada; não altera nem reinicia a instalação 0.1.13 em uso.

## Diagnóstico e contrato STT

A configuração consultada usa `openai/whisper-large-v3-turbo`, português, PCM16 mono a 16 kHz e ZDR. A 0.1.13 enviava blocos fixos de 5 segundos sem sobreposição, pedindo somente `json`. Não havia timestamps/confiança no contrato consumido pelo app. Cortes de palavras e perda de contexto são uma causa plausível do relato, sem comprovar que expliquem todos os erros: não há áudio bruto privado arquivado para análise posterior e não foi feita inferência autenticada de teste.

O OpenRouter documenta `verbose_json`, timestamps opcionais e confiança opcional, variáveis conforme provedor/modelo. A resposta não garante esses campos. A nova gravação pede `word` e `segment` para a família Whisper conhecida e lê somente metadados efetivamente retornados; outros modelos mantêm o pedido `json`. O contrato habitual permanece inalterado. [Documentação STT do OpenRouter](https://openrouter.ai/docs/guides/overview/multimodal/stt).

O Groq anuncia timestamps por palavra para Whisper e metadados de segmento como `avg_logprob` e `no_speech_prob`. Esses indicadores não são convertidos pelo app em uma probabilidade inventada de correção. Nenhum texto é eliminado por baixa confiança, por RMS ou por VAD. Confiança, quando presente e válida entre 0 e 1, é guardada como metadado informado pelo provedor. [Documentação Groq](https://console.groq.com/docs/speech-to-text).

Não foi confirmado por inferência real se a rota usada em cada chamada retornará todos os campos. Metadados ausentes ou inválidos preservam o texto e deixam a borda para revisão. Um erro de formato do provedor interrompe com aviso, sem repetição automática nem relaxamento de política.

## Janelas, consolidação e recuperação

O padrão proposto usa **12 segundos, sobreposição de 3 segundos e prévia inicial aos 6 segundos**. A prévia é provisória. A janela maior reprocessa esse início com mais contexto; a hipótese anterior continua arquivada e deixa de ser copiada duas vezes. Depois, uma nova janela é formada a cada 9 segundos. Parar envia a cauda parcial somente se houver amostras novas; parar exatamente no fim de uma janela não reenvia apenas a sobreposição.

O núcleo anterior à próxima sobreposição pode ser consolidado usando os tempos reais das palavras. O trecho de borda permanece provisório. A reconciliação exige palavras equivalentes na mesma posição temporal do áudio recebido: usa concordância contígua e verificação de ambiguidade; uma âncora curta exige intervalos quase idênticos e correspondência temporal única. Não usa remoção global de frases repetidas, similaridade vaga, correção de números ou estimativa de confiança. Repetições em instantes diferentes permanecem.

Quando o alinhamento não é seguro, o núcleo já consolidado é preservado e as alternativas de borda ficam identificadas para revisão. O app não escolhe silenciosamente qual fala apagar. Por isso podem existir trechos sobrepostos explícitos na área de revisão, em vez de uma transcrição falsamente “limpa”. Todas as hipóteses originais ficam acessíveis em **Trechos originais preservados**. Essa é uma proteção de integridade, não uma garantia de precisão dos timestamps remotos.

O journal continua somente com texto e metadados. Guarda hipóteses originais antes das revisões; cada conjunto de revisões é uma linha atômica com flush. Uma linha final incompleta é descartada na recuperação, preservando as hipóteses completas anteriores. Rascunhos recuperados ficam marcados para revisão. Áudio existe apenas em RAM e é limpo após consumo/encerramento. Sessões antigas 0.1.13 permanecem legíveis. Novas sessões usam `<id>.v2.jsonl`, de modo que um leitor 0.1.13 ignore o novo formato em um eventual downgrade em vez de truncá-lo por desconhecer registros de revisão.

A fila limita áudio pendente a 120 segundos de payload, incluindo sobreposições. Excesso interrompe explicitamente a captura, sem expulsar trabalho anterior. Falha remota não gera uma nova tentativa do mesmo bloco nem novo envio final motivado pela falha. Texto retornado durante falha de disco continua disponível em memória para copiar. Há uma única captura e um consumidor STT; STT, resposta e Rewrite compartilham uma única vaga de inferência no contador da sessão. Cancelamento e fechamento recusam resultados tardios.

## Comparação controlada

Na fixture de 47 segundos, com palavras cruzando bordas, pausas, frases repetidas, números e datas:

| Perfil | Primeira prévia* | Atualização contínua* | Requisições | Áudio submetido | Erros de borda no mock |
| --- | ---: | ---: | ---: | ---: | ---: |
| 5 s / sem overlap | 5 s | 5 s | 10 | 47 s | 10 |
| 12 s / overlap 3 s / prévia 6 s | 6 s | 9 s | 6 | 65 s | 0 |
| 18 s / overlap 4 s / prévia 6 s | 6 s | 14 s | 5 | 65 s | 0 |

\*Tempos de captura, aos quais se somam a latência do serviço e eventual fila. O núcleo começa a consolidar após a janela completa, com timestamps utilizáveis. A tabela não mede latência nem WER do provedor: o mock introduz erros controlados nas palavras cortadas para exercitar a reconciliação. Não demonstra melhora percentual de reconhecimento real. O perfil 12/3 foi escolhido por atualizar antes de 18/4, oferecendo mais contexto que 5/0. Em regime contínuo, o overlap aumenta o áudio submetido em cerca de um terço; cobrança real depende da rota e não foi estimada.

Uma fixture falada em pt-BR, gerada por SAPI diretamente em arquivo, confirmou preservação de cada amostra PCM através das janelas e do stop final. Outra verificação percorreu aproximadamente quatro minutos com ordem exata de palavras. Nenhuma fixture foi enviada a um provedor real. Os testes novos são temporários, fora do repositório.

## Interface

O painel STT tem 380 DIP de largura, como o popover de resposta, fica ancorado abaixo do círculo e acompanha sua posição. Não tem barra de título solta. Mostra Consolidado, Provisório/Bordas a revisar, seleção do histórico, originais e copiar. O pequeno controle abaixo do ícone mostra círculo vermelho Rec ou quadrado Stop; a caneta abre uma entrada rápida Rewrite. Os controles de fundo não ativam outra janela automaticamente. A entrada Rewrite ganha foco somente quando aberta pelo usuário. Menus e painéis mantêm fechamento externo, preservando o conteúdo.

## Rewrite canônico e modelo de resposta

A composição é exatamente a do handler local do LMDocs para Revisão (`proofread`), estilo Padrão e remoção de elementos típicos de IA: `global_natural_writing`, `rewrite_base`, `rewrite_mode_proofread`, `rewrite_style_default`, `rewrite_ai_patterns_skill`, nessa ordem e separados por duas quebras de linha. Não recebe instruções adicionais de resposta, pesquisa, chat anterior ou transcrição automática.

`scripts/Sync-RewritePrompts.ps1` importa as fontes canônicas sem frontmatter, normaliza quebras de linha e registra hashes dos arquivos, corpos e composição. O bundle é incorporado ao binário e validado em runtime. O build verifica a composição e o handler quando o checkout LMDocs está disponível; deriva exige revisão e sincronização explícita. A distribuição não depende de um checkout LMDocs instalado nem altera o outro repositório. Essa é uma snapshot versionada da fonte local; o banco remoto de prompts do LMDocs não foi consultado.

O identificador exato `deepseek/deepseek-v4.1-flash-1017` foi encontrado na API pública, no `GET /models/user` da própria conta e na lista pública ZDR. A consulta autenticada foi somente de leitura e não expôs a chave. A política atual continua `zdr=true`, `data_collection=deny`; se não houver rota compatível, a requisição falha. ZDR por requisição não desativa imposições mais estritas da conta. [Política ZDR](https://openrouter.ai/docs/guides/features/zdr), [lista oficial de endpoints ZDR](https://openrouter.ai/api/v1/endpoints/zdr).

O modelo passa a ser o padrão de resposta/Rewrite em novas configurações da preparação 0.1.14. Novos padrões exigem ZDR; valores explícitos de configurações existentes são preservados. A migração da configuração atual foi preparada separadamente para alterar somente `AnswerModel`, mediante a futura instalação autorizada. O STT e JEV não são trocados. A instalação em uso continua com sua configuração anterior. Disponibilidade de saldo, qualidade e latência da inferência real não foram testadas.
