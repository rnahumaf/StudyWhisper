# Indicador vetorial — 0.1.4

O círculo permanece no canto superior direito. Ao chegar uma atividade real, abre uma faixa de 360 × 104 DIP com áudio, filtro, Whisper, JEV e resposta. Recolhe após três segundos do resultado; continua aberto enquanto avalia um trecho ou processa uma chamada. O círculo compacto ocupa 72 × 72 DIP. A borda direita permanece na mesma posição ao expandir e recolher.

## O que as cores mostram

Uma FFT radix-2 de 1.024 pontos usa janela Hann de 64 ms, áudio PCM16 mono a 16 kHz e atualização a cada 100 ms. Ciano representa graves de 80–350 Hz; violeta, médios de 350–2000 Hz; âmbar, agudos de 2–8 kHz. Energia de cada banda é normalizada pelo total, multiplicada pela intensidade RMS em escala logarítmica. RMS abaixo de 0,001 apaga as bandas. Assim, silêncio não ganha brilho por normalização. A análise usa apenas 26.624 bytes de buffers numéricos fixos e não envia dados.

As cores não indicam intenção, pergunta, identidade ou humor. Silero continua sendo o filtro local de probabilidade de fala. O JEV recebe texto após a transcrição. Pontos de movimento nos conectores e na nuvem indicam atividade da etapa; não medem porcentagem concluída nem mostram raciocínio interno. Os glifos `Aa · _` no Whisper são decoração, sem exibir o que foi ouvido.

## Etapas reais

| Evento | Indicador |
| --- | --- |
| Episódio acústico começou | Linha pontilhada e filtro em avaliação |
| Silero detectou fala | Filtro aguarda a pausa/margem de término |
| Segmento terminou e foi aprovado | Check no filtro; linha iluminada para STT |
| Episódio sem fala terminou | X no filtro, sem chamada STT |
| STT foi iniciado | Caneta e glifos no Whisper |
| Transcrição chegou | Check no Whisper; JEV avalia texto |
| STT retornou vazio | X no Whisper, sem JEV |
| JEV ignorou | X no JEV, sem modelo de resposta |
| JEV aguardou ou ficou abaixo do limiar | Ícone de espera no JEV |
| JEV aprovou responder | Check no JEV; chamada textual de resposta |
| Resposta em processamento | Nuvem com movimento e rótulo neutro |
| Resposta chegou | Check na resposta; popover do histórico |
| API falhou | Aviso na etapa que falhou e tooltip acionável |
| Pausar/limpar | Geração cancelada, buffers apagados, movimento interrompido |

Silêncio isolado e uma fala ainda incompleta não produzem X. O limite de 30 segundos fecha um episódio contínuo sem fala; uma fala detectada segue os limites e sobreposição existentes. Cancelar um candidato incompleto conta como buffer cancelado, sem inflar o contador de rejeição local.

A fila mantém até duas falas por 30 segundos. O contador `fila n/2` fica visível; tooltip informa overflow ou expiração. Entrada nova, ruído ou mudanças da fila não substituem a etapa de uma chamada já em andamento. A sequência de publicação é serializada; identidade do pipeline e geração recusam eventos atrasados após pausar, limpar ou salvar configurações. Os eventos visuais não carregam PCM, transcrição, resposta ou texto de exceção.

## Movimento, foco e acesso

**Reduzir movimento do indicador** fica em Configurações. Também é aplicada a preferência de animações do Windows, consultada em `SystemParameters.ClientAreaAnimation`, sem alterar o sistema. Nesse modo, formas, texto e resultados permanecem; órbitas, pontos em trânsito e deslocamento de glifos param. As bandas ainda atualizam os valores medidos em até 10 Hz.

O timer decorativo roda em até 15 Hz apenas durante avaliação, chamada ou atividade sonora. Dados de espectro são coalescidos: só um callback de atualização pode ficar pendente no dispatcher. Em repouso/pausa não há timer contínuo. Não há shader, imagem de rede ou sombra custosa. Formas WPF, glifos MDL2, nomes acessíveis, tooltip e texto complementam as cores. Leitor de tela completo não foi testado.

Clique no círculo abre o histórico. Botão direito abre histórico, configurações, monitoramento, limpar e sair. A bandeja mantém os mesmos comandos. A expansão, o resultado e o popover conservam `WS_EX_NOACTIVATE`, `ShowActivated=false` e o HWND em primeiro plano. Configurações ganha foco somente por ação do usuário.

## Evidências e limites

`scripts/Test.ps1 -Ui` valida frequências sintéticas de 125/1000/4000 Hz, silêncio, intensidade, reset, rejeição após término, cancelamento, fala TTS real no Silero com APIs simuladas, ordem das etapas, espera/erro/ignorar, fila e geração atrasada. `artifacts/spectrum-audit.json` mede 60 s de PCM sintético no PC de referência; não é benchmark de notebook fraco.

As capturas `artifacts/ui-smoke/flow-*.png` são renderizações das janelas nativas, com eventos de fixtures. Há tamanho compacto, faixa de 360 DIP, variante de 300 DIP e rasterização a 96/120/144/192 DPI. Essas rasterizações verificam escala vetorial; não equivalem a mover janelas entre monitores físicos com configurações diferentes. Não houve mudança do DPI, das animações ou de certificados no Windows.

A demonstração `StudyWhisper.exe --demo` oferece pergunta, comentário, fala incompleta, pausa e limpeza, com senoide local e APIs fictícias. Ela usa o detector RMS legado, não comprova Silero ou classificação real. A integração neural é exercitada pelos testes com TTS; capturas pessoais e APIs autenticadas não foram usadas. Instalação/desinstalação, bandeja via UI Automation, mouse físico/hover, acessibilidade assistiva, Windows limpo, custo e desempenho em notebook fraco permanecem pendentes.

Fontes oficiais verificadas em 03/10/2026: [glifos MDL2](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font) (`E71C` Filter, `E70F` Edit, `E711` Cancel, `E73E` CheckMark, `E753` Cloud) e [consulta da preferência de animações no código WPF](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/SystemParameters.cs). Endpoints, modalidades e IDs de modelos seguem [API.md](API.md).
