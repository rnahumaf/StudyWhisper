# Verificação — 03/10/2026

## Atualização 0.1.1

Popover compactado: largura de 410 para 380 DIP, altura ajustada ao conteúdo com teto de 320 DIP; padding de 16 para 12 DIP; copiar em ícone de 26 DIP junto ao horário, separadores menores e ações de limpar/fechar no cabeçalho. Hover, relógio, histórico, rolagem e proteção de foco foram preservados. Captura e pipeline permanecem iguais.

34 testes de core continuam passando. O smoke agora contém 15 verificações, incluindo duas respostas curtas sem rolagem desnecessária, nomes/tooltip dos ícones, cópia da resposta certa usando callback de fixture (sem alterar clipboard pessoal) e ações de limpar e fechar. As renderizações foram inspecionadas e comparadas às fixtures da versão anterior. O audit runner mediu exportação de áudio em cinco cenários sintéticos; consulte `AUDIO-COST-AUDIT.md`.

A compilação foi realizada em uma cópia isolada. O instalador 0.1.1 é entregue separadamente e não foi executado; o app em uso não foi encerrado, pausado ou atualizado. Chave/configuração pessoal e conversas não foram lidas. STT local foi apenas avaliado e documentado; nenhum modelo foi baixado.

## Verificação inicial 0.1.0

Build WPF .NET 8 concluído sem erros ou avisos. Pacote autossuficiente `win-x64`, instalador Inno Setup e SHA-256 ficam em `artifacts`. O código não foi publicado no GitHub e os outros projetos não foram modificados.

O EXE autossuficiente também executou as dez verificações nativas com `DOTNET_ROOT` apontando para uma pasta vazia e consulta a runtimes globais desabilitada. O hash do instalador foi comparado ao arquivo `.sha256` após cópia ao destino. O repositório tem HEAD inicial em `main`, sem commits ou remotos; não foi feito commit sem pedido específico.

## Cobertura automatizada

34 testes passaram: silêncio e buffer limitado, áudio senoidal sintético, WAV PCM16, ruído breve, segmentação máxima, reset, limites de configurações, modalidades do catálogo, JEV válido e malformado, pausa inicial, sequência STT→JEV→chat, comentários/interjeições, transcrição vazia, probabilidade insuficiente, continuação/expiração de fala, pausa nas três etapas, resultado atrasado, concorrência, limpeza, memória habilitada/desabilitada, histórico limitado, parágrafo curto, relógio de fechamento, orçamento por minuto/sessão, cooldown, contratos HTTP e políticas ZDR, validação e catálogo sem inferência, erros redigidos, timeout durante leitura do corpo e DPAPI com segredo fictício em diretório isolado.

Smoke de WPF com janelas reais e fixtures: círculo pausado, caneta, X, pensamento; histórico vazio, resposta e histórico longo; configurações em tamanho normal e mínimo com rolagem até os limites. Dez verificações automáticas passaram, incluindo bits `WS_EX_NOACTIVATE` e manutenção do HWND em primeiro plano ao mostrar círculo, histórico e resposta. As renderizações de conteúdo foram inspecionadas; ficam em `artifacts/ui-smoke`, sem dados pessoais.

Essas capturas são renderizações WPF, não screenshots de toda a área de trabalho. Não há automação de cliques nativos ou validação independente de bandeja via UI Automation. Os testes de hover/tempo e foco cobrem a lógica e o estado de janela, sem demonstrar cada interação de mouse em um desktop de outro usuário.

## Não verificado ao vivo

- Captura de microfone real, permissões, drivers e qualidade do VAD em ambientes ruidosos.
- Validação de chave real, saldo, inferência STT/JEV/chat, latência, qualidade das respostas e precisão do limiar 0,65.
- Disponibilidade de rotas compatíveis com ZDR para a conta e os modelos escolhidos; o pedido mantém a política em caso de erro.
- Instalação/desinstalação no perfil pessoal, login automático do Windows, experiência de SmartScreen e assinatura de código. O instalador está sem assinatura.
- Múltiplos monitores, DPI além do ambiente corrente, fullscreen exclusivo e acessibilidade com leitores de tela.

Não foi aberto microfone pessoal, procurada credencial existente ou executada chamada autenticada real. O MVP entrega a implementação dessas integrações e uma demonstração offline; os itens acima precisam de aceite manual posterior.

## Versão 0.1.2 — Silero e contadores

54 testes de núcleo/mocks passam; 16 verificações WPF passam. O EXE autossuficiente passa mais 2 verificações de ONNX CPU/modelo incorporado e as 16 de WPF com DOTNET_ROOT indisponível e lookup global desativado. Isso comprova o pacote no PC de referência, incluindo suas dependências nativas disponíveis; não comprova um Windows limpo sem Visual C++ runtime.

Evidências: `artifacts/0.1.2-core-test-results.txt`, `artifacts/0.1.2-test-results.txt`, `artifacts/0.1.2-build-results.txt`, `artifacts/0.1.2-audio-triage-audit.json`, `artifacts/ui-smoke-0.1.2`, `artifacts/package-audio-smoke-0.1.2`, `artifacts/package-ui-smoke-0.1.2`.

A comparação offline reduziu o tom contínuo 220 Hz de 60 s/4 segmentos para 0,704 s/1 segmento, com falso positivo inicial. As fixtures TTS pt-BR conservaram toda amostra de amplitude absoluta >16 nas margens exportadas, incluindo “por quê?” e ganho 0,1. Não houve microfone pessoal, chamadas autenticadas, custo real observado ou teste de acurácia semântica do JEV. [Método, licença, CPU, métricas e limites](AUDIO-TRIAGE.md).

Fontes e pacote foram obtidos com escalonamentos aprovados automaticamente. Não houve rejeição automática de aprovação. A geração do instalador não instala nem fecha a sessão ativa. O build inicial do SDK exibiu geração automática de certificado ASP.NET; não foi lido/usado, e os scripts passaram a desabilitar essa geração. Nenhum acesso a chave pessoal, cofre ou microfone foi feito.

A fila limitada foi verificada com duas perguntas consecutivas enquanto a primeira aguardava STT, overflow preservando WAV recente, expiração, pausa/retomada e limpar com provedor não cooperativo, além da quota HTTP compartilhada. Nenhum WAV pendente antigo foi enviado após retomar. Essas verificações usam mocks, sem credencial real.


## Versão 0.1.3 — pré-requisito nativo

62 testes de núcleo/mocks e 21 verificações WPF passam. A ausência de C++ total/parcial e falhas de loader/arquitetura/símbolos foram simuladas sem alterar o sistema; o aviso tem link oficial, preserva foco e mantém monitoramento pausado. O EXE empacotado tem modos dependency-smoke, audio-smoke e ui-smoke para verificar simulação, ONNX real disponível e renderização. Veja [portabilidade](PORTABILITY.md) para evidências e limites. Não houve instalação de runtime, leitura/alteração de certificados, microfone ou inferência autenticada. O 0.1.2 e a sessão ativa permanecem preservados.


## Versão 0.1.4 — indicador vetorial

77 testes de núcleo/mocks e 39 verificações de WPF passam: FFT por banda/silêncio, cancelamento sem falso descarte, etapas reais, TTS neural com APIs simuladas, espera/ignorar/erro, fila, resultados atrasados, opção de movimento, limite de redesenho, recolhimento e foco. Capturas nativas em 72/360/300 DIP e rasterização a 96/120/144/192 DPI; isso não é teste físico em múltiplos monitores. A auditoria da FFT, após aquecimento, está em artifacts/spectrum-audit.json; inclui 600 análises para 60 s, sem alocações gerenciadas no loop, no PC de referência. Veja [monitor visual](VISUAL-MONITOR.md) para definições e fontes oficiais.

Evidências finais: artifacts/0.1.4-test-results.txt, artifacts/0.1.4-build-results.txt, artifacts/0.1.4-spectrum-audit.json, artifacts/ui-smoke-0.1.4 e artifacts/package-ui-smoke-0.1.4. O pacote autossuficiente passa ONNX real disponível (2), pré-requisito simulado (3) e WPF (39), com DOTNET_ROOT indisponível. Nenhuma instalação, atualização da sessão ativa, chamada autenticada, microfone pessoal, certificado ou preferência global foi alterado. Versões anteriores preservadas. Windows limpo sem C++, notebook fraco, instalação e APIs reais permanecem pendentes.


## Versão 0.1.5 — círculo único

77 testes de núcleo/mocks e 61 verificações WPF passam. O círculo conserva tamanho 72 × 72 DIP e âncora em todas as cenas; nenhuma faixa ou nó externo aparece. Pixels externos ao recorte circular são transparentes, inclusive com rasterização 96/120/144/192 DPI. São verificados fade sem sobreposição, filtro sem X prematuro, etapas JEV distintas, fila mantendo cena remota, redução de movimento, 15 Hz, retorno à escuta, cancelamento e foco. A comparação lens-scenes.png contém legendas de evidência externas ao indicador; essas legendas não fazem parte do app.

Evidências: artifacts/0.1.5-test-results.txt, artifacts/ui-smoke-0.1.5, artifacts/0.1.5-build-results.txt e artifacts/package-ui-smoke-0.1.5. O EXE autossuficiente passa novamente 3 verificações de pré-requisito simulado, 2 de ONNX real disponível e 61 de WPF; scripts/Verify-Package.ps1 reproduz esses checks. A 0.1.4 fica preservada em instalador/EXE e ZIP de fontes. APIs, áudio e custos reais, instalação, Windows limpo, notebook fraco e múltiplos monitores físicos continuam pendentes. Nenhum app ativo foi reiniciado, nenhuma instalação ou publicação foi feita, nenhum microfone/chave pessoal ou certificado foi acessado.


[Verificação 0.1.6: conversa, pesquisa, Markdown e limites](VERIFICATION-0.1.6.md).
