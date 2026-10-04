# Conversa, pesquisa e apresentação, 0.1.6

## Contexto e estilo

O pipeline seleciona os dez últimos pares completos antes da pergunta atual. Esse mesmo snapshot é enviado como `state.recent_context` ao JEV e como mensagens alternadas user/assistant à resposta. A pergunta atual aparece uma vez. Somente uma resposta concluída da geração atual entra no histórico; ignorados, espera, falhas, cancelamento e resultados antigos não entram. Limpar apaga histórico, continuação e filas. Desativar memória envia contexto vazio e mantém o histórico local visível.

`AnswerStyle` adapta orientações conferidas na página-fonte local `RodrigoNahum/src/artigos/escrita-natural-para-agentes/index.html`, indicada pela skill `rn-natural-writing`. A [referência pública](https://www.rodrigonahum.com.br/artigos/escrita-natural-para-agentes/) reúne os critérios. A implementação usa texto próprio, sem copiar o artigo inteiro ou depender daquele repositório durante execução. O padrão é curto e direto, com suposições razoáveis, incerteza factual concreta e preservação de condições clínicas importantes. Pedidos explícitos de formato ou extensão prevalecem sobre o padrão de um parágrafo.

Resposta tem limite de 1.400 tokens e apresentação limitada a 6.000 caracteres, preservando quebras e Markdown. Histórico mantém até 30 pares em RAM. Modelos podem não seguir as instruções; os testes verificam payload e fluxo, não qualidade editorial real.

## Busca oficial e fontes

Contrato consultado em 04/10/2026: [Web Search Server Tool](https://openrouter.ai/docs/guides/features/server-tools/web-search) e [Server Tools e retenção](https://openrouter.ai/docs/guides/features/server-tools). Usa `tools: [{type: "openrouter:web_search", parameters: {engine: "exa", max_uses: 1, max_results: 3, max_total_results: 3, max_characters: 2000}}]`, `tool_choice: "auto"` e `max_tool_calls: 1`. OpenRouter executa o ciclo no servidor; não há rodada adicional implementada pelo cliente, `:online` acrescentado ou plugin de busca legada.

A ferramenta fica disponível apenas na resposta, se habilitada e sem ZDR. O modelo decide executar zero ou uma consulta. O prompt orienta busca para fatos atuais, fontes verificadas ou dúvida relevante, sem buscar todo conceito estável. O engine Exa permite aplicar os limites sem depender de limites ignorados por alguns provedores nativos. `usage.server_tool_use.web_search_requests` informa consultas quando fornecido. `message.annotations` do tipo `url_citation` fornece até três fontes distintas com título e URL HTTP/HTTPS válida. Os índices da citação não são usados para alterar texto truncado. Custo adicional não é calculado com tabela fixa.

ZDR é mantido em STT, JEV e resposta e bloqueia a ferramenta de busca. As políticas de retenção do mecanismo de pesquisa são independentes. Preferência de pesquisa pode permanecer marcada para uso futuro, mas o controle fica indisponível enquanto ZDR estiver marcado. Modelos com pesquisa intrínseca não são auditados pelo app. Falhas não provocam tentativas automáticas nem relaxamento de política. A API de server tools está em beta.

## Markdown nativo

Markdig MIT analisa CommonMark com HTML desativado. A AST é transformada em TextBlock, Run, Bold, Italic, Hyperlink e painéis WPF para parágrafos, listas e código. Não há HTML host, script, imagem remota ou arquivo embutido. Links aceitam somente HTTP/HTTPS absoluto, sem credenciais no endereço, e abrem por ação explícita; testes injetam um callback sem navegador. HTML permanece texto literal. Tabelas e extensões avançadas não fazem parte deste MVP.

O corpo cresce até 252 DIP, incluindo pergunta e metadados discretos, aproximadamente dez linhas úteis de resposta de 20 DIP. Conteúdo adicional e histórico rolam; respostas curtas diminuem a altura. Posicionamento usa DIP e limita a janela à área de trabalho disponível. Copiar converte a AST para texto com parágrafos, listas e código, sem marcadores de formatação. Hover/interação suspendem o relógio, com pausa validada por teste de lógica; não houve injeção de mouse no desktop pessoal.

## Diagnóstico de espera por chamadas

No relato de 04/10, várias falas ignoradas foram seguidas por perguntas que exibiram alerta e voltaram a funcionar após cerca de um minuto. Não existia log técnico persistido para comprovar a origem: somente arquivos de preferências e DPAPI estavam presentes, cujos conteúdos não foram lidos. Não é possível atribuir esse episódio a HTTP 429 ou limite local com certeza.

Foi reproduzido com mocks um limite local após falas negativas, seguido por perguntas repetidas e recuperação após 60 segundos. Falas aprovadas pelo VAD mas ignoradas pelo JEV custam duas chamadas; antes, o mesmo `ApiException` e triângulo representavam limites e erros. A atualização usa `ThrottleException`, estado de espera distinto de descarte e falha, origem e prazo no tooltip, e diagnóstico atualizado em Configurações. Verifica capacidade para três chamadas antes do STT para evitar gasto parcial quando não pode concluir a pergunta. Isso pode recusar um novo segmento mesmo com uma ou duas chamadas disponíveis. Não aumenta limites, não contorna o provedor e não guarda áudio para reenvio tardio.

HTTP 429 respeita `Retry-After`, inclusive data absoluta, com fallback de 60 s e faixa de 1 a 3.600 s. Limites de sessão não recuperam com o tempo. Nenhum caso provoca repetição automática. A fila continua limitada, cancelável e com expiração. Caso ocorra novamente, o texto do tooltip e o diagnóstico de Configurações distinguem as causas sem expor conteúdo ou chave.
