# Indicador circular — 0.1.5

O único indicador de processamento é um círculo fixo em uma janela transparente de **72 × 72 DIP**, no canto superior direito. Filtro, transcrição, JEV e geração aparecem como cenas alternadas dentro desse círculo, como uma lupa do processamento. Tamanho e posição não mudam entre etapas. O popover de resposta/histórico permanece separado, abaixo do círculo.

## Cenas internas

| Evento real | Cena no círculo |
| --- | --- |
| Pausado ou cancelado | Pausa, sem animação contínua |
| Escuta sem trecho em avaliação | Microfone e espectro medido |
| Episódio acústico começou / fala ainda incompleta | Três linhas pontilhadas até uma catraca/filtro interno |
| Segmento aprovado pelo filtro | Visto com contexto “Filtro” |
| Episódio sem fala terminou | X com contexto “Filtro” |
| STT iniciou | Caneta, glifos decorativos `Aa · _` e cursor |
| Transcrição recebida / vazia | Visto ou X com contexto “STT” |
| JEV avaliando texto | Processamento em pequeno losango, contexto “JEV” |
| JEV ignorou / aguardou / aprovou | X, espera ou visto no contexto “JEV” |
| Chamada de resposta em andamento | Nuvem com órbita, contexto “Texto” |
| Resposta recebida | Visto com contexto “Texto”; popover abre |
| Falha | Aviso; motivo acionável no tooltip |

Cada desenho usa somente a cena selecionada. Não há diagrama, faixa horizontal ou coleção de nós fora do círculo. O recorte externo circular tem raio 31,5 DIP; os elementos internos são recortados em raio 25 DIP. A entrada da cena atual faz um fade de 150 ms, sem sobrepor o desenho da cena anterior. O resultado fica por três segundos e volta à escuta; chamadas e avaliação incompleta continuam visíveis enquanto necessárias.

Silêncio isolado ou um candidato ainda incompleto não gera X. Cancelar um candidato conta como buffer cancelado. O filtro não identifica pergunta ou intenção; somente o JEV textual, após STT, decide ignorar/aguardar/responder. A cena de geração representa atividade da chamada, sem porcentagem de conclusão ou raciocínio textual interno.

## Espectro e fila

As barras da borda vêm da FFT Hann radix-2 de 1.024 pontos: janela de 64 ms e cálculo a cada 100 ms sobre PCM16 mono a 16 kHz. Ciano: graves 80–350 Hz; violeta: médios 350–2000 Hz; âmbar: agudos 2–8 kHz. A energia relativa por banda é multiplicada pela intensidade RMS logarítmica; RMS abaixo de 0,001 apaga as bandas. Cores não representam identidade, intenção ou humor. Não há valores aleatórios para simular áudio.

Durante uma chamada, entrada nova ou descarte local não substitui a cena remota ativa. A fila mantém até duas falas por 30 segundos; um pequeno número interno indica a quantidade, e o tooltip informa overflow/expiração. Os eventos continuam tipados e sem áudio, transcrição, resposta ou texto de erro. Publicação serializada, identidade do pipeline e geração impedem resultados atrasados após pausa/limpeza/configuração. Captura, modelos, modalidades, endpoints e orçamentos não mudaram nesta correção de UX.

## Movimento e foco

**Reduzir movimento do indicador** e a preferência de animações do Windows desativam fade, catraca animada, cursor intermitente e órbitas. Formas, texto de contexto, resultados e valores medidos permanecem. O timer decorativo está limitado a 15 Hz e para em repouso/pausa; espectro mantém até 10 Hz, com um único callback pendente no dispatcher. Não há shader, download de imagem ou sombra custosa.

Tooltip e nomes acessíveis complementam formas e cores. Clique abre histórico; botão direito e bandeja oferecem histórico, configurações, monitoramento, limpar e sair. Círculo/popover mantêm `WS_EX_NOACTIVATE`, `ShowActivated=false` e o HWND em primeiro plano. Configurações ganha foco somente por ação do usuário. O preflight Visual C++ da 0.1.3 permanece antes da captura e da leitura da chave.

## Verificação e evidências

`scripts/Test.ps1 -Ui` executa 77 testes de núcleo/mocks e 61 verificações WPF. As cenas nativas são capturadas individualmente em `artifacts/ui-smoke-0.1.5/lens-*.png`, com comparação em `lens-scenes.png`. As legendas dessa comparação são do documento de evidência; não aparecem fora do círculo no aplicativo. Dados são exclusivamente fixtures e PCM sintético. `--demo` continua oferecendo pergunta, comentário, fala incompleta, pausa e limpeza, com detector RMS legado e APIs fictícias.

São verificados tamanho/âncora invariáveis, pixels transparentes fora do círculo em todas as cenas, transição, espera/descartes distintos, fila sem substituir a cena, movimento reduzido, limite de redesenho, retorno à escuta, geração antiga recusada, foco e comandos. As capturas a 96/120/144/192 DPI verificam rasterização vetorial, sem mudar o DPI global. Não demonstram troca entre monitores físicos.

O EXE empacotado tem os mesmos modos de smoke: ONNX local disponível (2), pré-requisitos simulados (3) e UI (61), com runtime .NET global indisponível. Instalação/desinstalação, Windows limpo sem C++, notebook fraco, bandeja via UI Automation, hover/mouse físico, leitor de tela, APIs autenticadas e microfone real permanecem pendentes. Nenhuma instalação, reinício do app ativo, publicação ou mudança de certificados/preferências globais foi realizada.

A 0.1.4 fica preservada nos seus EXE/instalador e em `artifacts/StudyWhisper-0.1.4-source.zip`; [contrato visual anterior](VISUAL-MONITOR-0.1.4.md) é histórico. Os [glifos oficiais MDL2](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font) e a [preferência de animações WPF](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/SystemParameters.cs) seguem os valores já verificados. Contratos de APIs continuam em [API.md](API.md).
