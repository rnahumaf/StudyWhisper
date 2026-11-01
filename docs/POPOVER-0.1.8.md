# Correção local do popover, 0.1.8

Uma resposta nova enquadra seu início e ajusta a altura ao conteúdo atual. Pergunta curta e primeiro parágrafo aparecem juntos; uma pergunta extensa não ocupa toda a área antes da resposta. Respostas anteriores continuam disponíveis ao rolar para cima. A reabertura após ocultar também mostra a resposta mais recente desde o começo.

O comportamento anterior podia preservar a posição antiga durante hover, mesmo com uma nova resposta, ou solicitar rolagem para o fim antes de o layout WPF estabilizar. A correção ancora o início da resposta, aplica a posição após o layout e invalida callbacks antigos ao chegar outro evento, ocultar a janela ou interagir. Uma atualização idêntica da janela já visível mantém a posição de leitura. Não há timer que reposicione o histórico continuamente.

90/90 testes de núcleo/mocks e 84/84 verificações WPF passaram. Dez cenários de regressão cobrem Markdown longo, início versus fim, resposta curta, várias respostas consecutivas, pergunta longa, reabertura, leitura voluntária, atualização idêntica, interação durante layout pendente, ocultação e foco. O teste antigo que exigia exibir duas respostas curtas juntas foi ajustado para a regra de mostrar primeiro a atual e manter a anterior acessível por rolagem.

Capturas nativas inspecionadas: `popover-new-long-start.png`, `popover-new-short.png`, `popover-history-voluntary.png` e `popover-consecutive.png`, em `artifacts/ui-smoke-0.1.8`. Não são capturas do desktop pessoal. Eventos de interação nos testes são WPF locais, sem injeção de entrada no sistema operacional.

Pacote local: `artifacts/StudyWhisper-0.1.8-Setup-x64.exe`, checksum SHA-256 e EXE autossuficiente em `artifacts/publish-0.1.8`. Para aplicar ao app instalado, é necessário atualizar a instalação e reabrir o app. Gerar o pacote não atualiza nem reinicia a sessão em uso. Não houve nova Release, commit ou push; a versão pública continua 0.1.7.

Testes usam mocks, áudio sintético e conversas fictícias. Não foram feitas chamadas pagas, captura de microfone pessoal, leitura de chave pessoal ou alterações de preferências. Instalação/atualização ao vivo e interação física de mouse em outras máquinas permanecem sem verificação.
