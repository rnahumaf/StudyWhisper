# StudyWhisper

Assistente de estudo para Windows que escuta quando você ativa o monitoramento e responde somente em texto. MVP 0.1.7, código aberto sob [licença MIT](LICENSE).

Correção local 0.1.8, ainda não publicada: novas respostas abrem desde o início, com histórico acessível por rolagem. O instalador local fica em `artifacts/StudyWhisper-0.1.8-Setup-x64.exe`. Veja [correção e testes do popover](docs/POPOVER-0.1.8.md). A versão pública descrita abaixo permanece 0.1.7.

## Instalar

Baixe `StudyWhisper-0.1.7-Setup-x64.exe` na [página de Releases](https://github.com/rnahumaf/StudyWhisper/releases). O instalador é por usuário e inclui .NET; não exige SDK ou administrador. O binário não tem assinatura digital.

Requisitos: Windows 10/11 x64, dispositivo de gravação, conexão com a internet e sua própria chave do OpenRouter. O filtro ONNX requer o [Microsoft Visual C++ v14 Redistributable x64](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170). Se o pré-requisito faltar, o app mostra instruções e permanece pausado. Consulte [portabilidade](docs/PORTABILITY.md).

1. Instale e abra o app. Todo início, inclusive com Windows, começa com monitoramento **desativado**.
2. Abra **Configurações** na bandeja ou no menu do círculo. Cole sua chave individual e clique **Validar e guardar chave**. A validação consulta a autenticação, sem gerar resposta.
3. Escolha modelos de transcrição, JEV e resposta. **Atualizar catálogo público** verifica modalidades sem enviar sua chave. O modelo de resposta começa sem seleção.
4. Ajuste memória, pesquisa, limites e tempo de fechamento. Clique **Salvar e pausar**, depois ative **Monitoramento** no menu.
5. Se desejar, marque **Iniciar com Windows**. O início automático também permanece pausado.

## Escuta e respostas

O círculo de 72 × 72 DIP fica no canto superior direito, acima das janelas. Suas cenas internas mostram filtro local, transcrição, JEV e geração; o espectro usa níveis reais de áudio. As cores representam energia sonora. A opção **Reduzir movimento** também respeita a preferência do Windows.

Silero VAD filtra silêncio e parte dos ruídos localmente. Trechos aprovados seguem para STT pelo OpenRouter; **somente o texto transcrito** chega ao classificador JEV. Perguntas claras de estudo geram resposta; comentários e interjeições são ignorados. Fala incompleta aguarda continuação por até 12 segundos. O filtro não identifica perguntas, TV ou falantes, e a precisão dos modelos reais em português depende da rota escolhida.

Por padrão, a resposta é um parágrafo curto. Pedidos explícitos podem gerar listas ou explicações mais longas. O popover cresce até aproximadamente dez linhas de resposta, depois permite rolagem. Parágrafos, negrito, itálico, listas, links e código são apresentados com controles WPF nativos. Não há navegador embutido, execução de HTML/scripts ou carregamento de imagens; links HTTP/HTTPS abrem somente por clique.

A resposta aparece sem ativar a janela. Hover e interação suspendem o fechamento; rolagem e clique reiniciam o prazo ajustável. Clique no círculo para reabrir o histórico. Ícones pequenos permitem copiar texto limpo, apagar a sessão e fechar. Memória habilitada envia os **dez últimos pares completos de pergunta e resposta**, mais a pergunta atual, tanto ao JEV quanto ao modelo de resposta. Falas ignoradas e respostas que falharam não entram nessa memória. Histórico visível guarda até 30 pares em RAM. Limpar cancela tarefas e apaga histórico e texto pendente.

## Pesquisa online e custos

**Permitir pesquisa online quando necessária** disponibiliza a ferramenta oficial `openrouter:web_search`. O modelo decide se precisa pesquisar dados atuais, fontes ou uma dúvida factual relevante; conceitos estáveis não devem gerar busca em todas as perguntas. Essa seleção depende do modelo e não garante ausência de consultas desnecessárias.

A implementação limita a uma consulta Exa por resposta e três resultados. Links de fontes retornados pela API aparecem discretamente no popover. Pesquisa, STT, JEV, resposta e contexto têm cobrança conforme a conta e os serviços usados. Não há assinatura ou saldo incluído no app. Os valores informados em `usage.cost` são mostrados quando disponíveis; cobertura incompleta permanece indicada, sem estimativa inventada. O contrato de busca está em beta e pode mudar.

Desde a versão local 0.1.12 não há teto interno de chamadas por minuto, sessão ou gasto. O app mantém contadores de tentativas e custos informados, sem bloquear por quantidade acumulada. Transcrição, JEV, resposta e pesquisa podem ser cobrados conforme o uso enquanto o monitoramento estiver ativo. Limites, saldo e HTTP 429 do provedor continuam valendo; a espera informada é respeitada, sem repetir automaticamente a solicitação. Validação da chave e catálogo público não entram na contagem de inferência.

Há uma tarefa ativa e até duas falas pendentes por 30 segundos. Excesso preserva a mais recente e apaga a pendente mais antiga. Pausar, limpar ou sair cancela o processamento, apaga buffers e invalida respostas atrasadas. Segmentos têm limite padrão de 15 segundos e chamadas expiram após 45 segundos. Cancelar uma chamada enviada não garante estorno.

## Privacidade

No modo habitual, áudio, transcrições e respostas não são gravados em disco. No modo Gravar, somente transcrição e metadados são persistidos localmente; áudio não é salvo. Veja [gravação 0.1.13](docs/RECORDING-0.1.13.md). Áudio aprovado vai ao STT; transcrição e contexto vão ao JEV e ao modelo de resposta. Com pesquisa habilitada, a consulta formulada pelo modelo também vai ao serviço de busca. O prompt orienta consultas curtas sem identificadores pessoais; as instruções proíbem copiar histórico bruto, conversa irrelevante, identificadores pessoais e segredos para a consulta. Isso não constitui garantia de anonimização ou de cumprimento por um modelo remoto.

A única chave usada é a cadastrada neste app, protegida pelo DPAPI do usuário em `%LOCALAPPDATA%\StudyWhisper\openrouter.dpapi`. Preferências ficam em `settings.json`, sem chave. O app não busca credenciais de outros programas, não tem telemetria nem logs de conversas. Strings gerenciadas e serviços remotos não permitem prometer remoção física imediata dos dados. Ao desinstalar, preferências e chave são preservadas; remova a chave pelo app antes se quiser descartá-la.

**Exigir ZDR e negar coleta** envia `provider.zdr=true` e `provider.data_collection=deny` em todas as etapas, sem relaxar a política em erros. A pesquisa Exa funciona com essa opção ativada e continua sob o controle **Permitir pesquisa online**. A busca envia consultas à Exa, cuja retenção não é coberta pelo ZDR do modelo. ZDR depende de rota compatível e pode impedir chamadas. Sem essa opção, aplicam-se as políticas da conta e dos provedores. Modelos com pesquisa intrínseca podem ter seu próprio comportamento; a política de retenção da busca deve ser considerada separadamente. Não foram auditadas as práticas dos serviços remotos.

## Desenvolver e testar

Instale .NET SDK 8 ou 10 e Inno Setup 6. As dependências são restauradas pelo NuGet oficial. Silero VAD 6.2.3 MIT está incluído, com hash verificado ao carregar; ONNX Runtime CPU 1.30.0 e Markdig 1.4.0 MIT são empacotados.

```powershell
.\scripts\Test.ps1 -Ui
.\scripts\Build.ps1
.\scripts\Verify-Package.ps1
.\artifacts\publish-0.1.7\StudyWhisper.exe --demo
```

Os testes usam mocks HTTP, relógio controlado e áudio sintético. O modo `--demo` não abre microfone nem faz inferência remota. `Test.ps1 -Ui` renderiza WPF real e verifica foco, rolagem, Markdown e círculo. `Build.ps1` produz EXE autossuficiente, instalador e SHA-256 em `artifacts`, fora do Git. `Verify-Package.ps1` verifica o pacote sem depender de .NET instalado globalmente.

Consulte [contratos oficiais](docs/API.md), [arquitetura](docs/ARCHITECTURE.md), [conversa, pesquisa e Markdown](docs/CONVERSATION.md), [testes do patch](docs/VERIFICATION-0.1.7.md), [testes e limites](docs/VERIFICATION.md), [triagem neural](docs/AUDIO-TRIAGE.md) e [indicador circular](docs/VISUAL-MONITOR.md). A versão 0.1.7 foi validada com simulações; microfone pessoal, chave real, inferência paga, pesquisa remota e instalação por outro usuário não foram testados ao vivo.

## Atualização local 0.1.9

JEV 1.14 passa a ser o padrão e integra o catálogo incluído, preservando o contrato de decisões e a opção manual de JEV 1.13. O instalador `artifacts/StudyWhisper-0.1.9-Setup-x64.exe` inclui as alterações locais 0.1.8 e cria o atalho StudyWhisper na área de trabalho para a instalação habitual. Esta atualização não foi publicada.

## Atualização local 0.1.10

Falhas transitórias da entrada de áudio iniciam reconexão automática com intervalos de 2, 4, 8, 16, 32 e até 60 segundos. Pausar ou sair cancela a reconexão. A recuperação local preserva o histórico e as perguntas completas já em processamento ou na fila; não repete chamadas ao OpenRouter. Trechos acústicos incompletos são reiniciados ao trocar a captura para não juntar dispositivos ou períodos diferentes. Formato incompatível ou filtro indisponível permanecem como bloqueios com orientação no indicador.

Menu do círculo, menu da bandeja e histórico fecham ao clicar fora, mantendo a função de fundo. O histórico continua acessível ao reabrir. Diagnóstico de captura guarda apenas data, etapa, número de tentativa, código nativo e classe de falha em um arquivo local limitado; não grava áudio, transcrições, respostas ou credenciais. Sessões e perguntas continuam apenas em RAM.

Pacote local: `artifacts/StudyWhisper-0.1.10-Setup-x64.exe`. O atalho único aponta para o caminho estável da instalação por usuário, atualizado pelo instalador. JEV 1.14, política de dados e configurações são preservados. Esta versão não foi publicada.

## Correção local 0.1.11

A entrada de áudio e os limites de envio agora têm indicadores separados. O microfone permanece visível quando chegam quadros, mesmo com os envios remotos bloqueados; a legenda “Limite” e o aviso explicam o motivo. Configurações e tooltip mostram quadros recebidos, processados e a idade do último quadro. Abrir o dispositivo sem receber dados não confirma escuta.

Foi corrigido o recorte negativo que podia ocorrer quando um segmento longo terminava com silêncio depois de um corte forçado. Caudas silenciosas já exportadas não geram outra solicitação. Uma falha isolada do filtro reinicia apenas o estado do filtro, mantendo a entrada de áudio aberta. Ausência de quadros por dois segundos aciona recuperação; a primeira reconexão espera 100 ms e falhas sucessivas chegam a no máximo cinco segundos entre tentativas. Pausar invalida o pipeline antes de aguardar o encerramento da captura.

O pacote local é `artifacts/StudyWhisper-0.1.11-Setup-x64.exe`. Nesta atualização autorizada, a transferência cifrada preserva histórico, orçamento usado e o estado de escuta; ela é removida após a retomada confirmada por quadros. O início normal continua pausado e sessões normais continuam apenas em RAM. A atualização não aumenta os limites de custo nem modifica JEV 1.14 ou políticas de dados.

## Remoção dos tetos locais, 0.1.12

Por autorização explícita do usuário, foram retirados os tetos de 12 chamadas/minuto e 300 por execução, a exigência de margem para três chamadas e seus campos de configuração. Nenhum número finito maior foi colocado em seu lugar. O contador e os custos acumulados são preservados na transferência de atualização. O app mantém fila, concorrência de uma solicitação por vez, cancelamento, limites técnicos de áudio/resposta, privacidade e esperas reais do provedor.

Pacote: `artifacts/StudyWhisper-0.1.12-Setup-x64.exe`. Os números citados nas notas 0.1.11 e anteriores descrevem comportamento histórico, substituído por esta versão.


## Gravação com transcrição ao vivo, 0.1.13

Gravar / Parar e Gravações estão no menu do círculo e da bandeja. O modo contínuo transcreve todos os quadros recebidos da fonte habitual, sem VAD ou JEV, e salva texto incrementalmente para consulta e cópia. Parar conclui os blocos pendentes e restaura o estado anterior do monitoramento. Sessões interrompidas preservam o texto confirmado. Não há áudio bruto em disco nem gravação automática. Consulte [comportamento, persistência e limites](docs/RECORDING-0.1.13.md). Pacote local preparado: `artifacts/StudyWhisper-0.1.13-Setup-x64.exe`; ainda não publicado ou instalado.

## Preparação local 0.1.14

Janelas STT de 12 segundos com overlap de 3, prévia aos 6, reconciliação por timestamps e separação entre texto consolidado e provisório. Painel ancorado, Rec/Stop direto e Rewrite com snapshot canônica versionada do LMDocs. Modelo padrão de resposta/Rewrite verificado: `deepseek/deepseek-v4.1-flash-1017`, com ZDR preservado; STT e JEV mantidos. A versão está preparada em cópia isolada e não foi instalada. Veja [diagnóstico, comparação controlada e limites](docs/RECORDING-0.1.14.md).

## Captura e reescrita local, 0.1.16

Ao parar a gravação, o app conclui os blocos de áudio pendentes e reescreve automaticamente toda a transcrição disponível. O painel mostra o texto final e os trechos originais, permite expandir para baixo ou ajustar a altura pelo puxador inferior e conserva a posição de leitura. É possível cancelar a reescrita ou solicitá-la novamente no próprio histórico.

A revisão usa o modelo de resposta configurado e pode gerar cobrança por parte de texto. Não faz pesquisa online. Números, negações e avisos de incerteza têm verificações conservadoras; a precisão semântica ainda depende do modelo e da revisão do usuário. Falhas preservam o original e não provocam repetição automática de chamadas.

Preparação local, sem instalação nem publicação: `artifacts/StudyWhisper-0.1.16-Setup-x64.exe`. Veja o [diagnóstico, comportamento e limites de validação](docs/RECORDING-0.1.16.md).


## Instalação local 0.1.17

Em 05/11/2026, a versão 0.1.17 foi instalada localmente com ícone próprio. Ao abrir o atalho com o aplicativo já em execução, a instância existente apresenta configurações ou gravações, conforme o estado. As notas de preparação anteriores descrevem o estado na data em que foram escritas. A validação de ativação usou demonstração offline e o atalho instalado; a interação durante uma gravação real não foi exercitada.
