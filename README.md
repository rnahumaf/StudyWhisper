# StudyWhisper

Assistente de estudo para Windows que escuta quando você ativa o monitoramento e responde somente em texto. MVP 0.1.6, código aberto sob [licença MIT](LICENSE).

## Instalar

Baixe `StudyWhisper-0.1.6-Setup-x64.exe` na [página de Releases](https://github.com/rnahumaf/StudyWhisper/releases). O instalador é por usuário e inclui .NET; não exige SDK ou administrador. O binário não tem assinatura digital.

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

Por padrão são 12 tentativas HTTP de inferência por minuto e 300 por execução. Uma pergunta usa até três chamadas; uma fala ignorada pelo JEV usa STT e classificação e **também consome o limite**. O app verifica espaço para três etapas antes de iniciar STT, sem consumir uma reserva antecipadamente. Ao atingir um limite, o círculo mostra espera e o tooltip informa limite local ou HTTP 429, prazo quando disponível e orientação. Configurações exibe o diagnóstico atualizado. Não há repetição automática nem reenvio de áudio recusado; faça a pergunta novamente após a espera. O limite de sessão exige aumentar o limite configurado ou sair e iniciar outra execução.

Há uma tarefa ativa e até duas falas pendentes por 30 segundos. Excesso preserva a mais recente e apaga a pendente mais antiga. Pausar, limpar ou sair cancela o processamento, apaga buffers e invalida respostas atrasadas. Segmentos têm limite padrão de 15 segundos e chamadas expiram após 45 segundos. Cancelar uma chamada enviada não garante estorno.

## Privacidade

Áudio, transcrições e respostas não são gravados em disco. Áudio aprovado vai ao STT; transcrição e contexto vão ao JEV e ao modelo de resposta. Com pesquisa habilitada, a consulta formulada pelo modelo também vai ao serviço de busca. O prompt orienta consultas curtas sem identificadores pessoais; isso não constitui garantia sobre o comportamento de um modelo remoto.

A única chave usada é a cadastrada neste app, protegida pelo DPAPI do usuário em `%LOCALAPPDATA%\StudyWhisper\openrouter.dpapi`. Preferências ficam em `settings.json`, sem chave. O app não busca credenciais de outros programas, não tem telemetria nem logs de conversas. Strings gerenciadas e serviços remotos não permitem prometer remoção física imediata dos dados. Ao desinstalar, preferências e chave são preservadas; remova a chave pelo app antes se quiser descartá-la.

**Exigir ZDR e negar coleta** envia `provider.zdr=true` e `provider.data_collection=deny` em todas as etapas, sem relaxar a política em erros. Essa opção **bloqueia a ferramenta de pesquisa**, porque a política do modelo não cobre a retenção do buscador. ZDR depende de rota compatível e pode impedir chamadas. Sem essa opção, aplicam-se as políticas da conta e dos provedores. Modelos com pesquisa intrínseca podem ter seu próprio comportamento; o bloqueio descrito refere-se à ferramenta adicionada pelo app. Não foram auditadas as práticas dos serviços remotos.

## Desenvolver e testar

Instale .NET SDK 8 ou 10 e Inno Setup 6. As dependências são restauradas pelo NuGet oficial. Silero VAD 6.2.3 MIT está incluído, com hash verificado ao carregar; ONNX Runtime CPU 1.30.0 e Markdig 1.4.0 MIT são empacotados.

```powershell
.\scripts\Test.ps1 -Ui
.\scripts\Build.ps1
.\scripts\Verify-Package.ps1
.\artifacts\publish-0.1.6\StudyWhisper.exe --demo
```

Os testes usam mocks HTTP, relógio controlado e áudio sintético. O modo `--demo` não abre microfone nem faz inferência remota. `Test.ps1 -Ui` renderiza WPF real e verifica foco, rolagem, Markdown e círculo. `Build.ps1` produz EXE autossuficiente, instalador e SHA-256 em `artifacts`, fora do Git. `Verify-Package.ps1` verifica o pacote sem depender de .NET instalado globalmente.

Consulte [contratos oficiais](docs/API.md), [arquitetura](docs/ARCHITECTURE.md), [conversa, pesquisa e Markdown](docs/CONVERSATION.md), [testes e limites](docs/VERIFICATION.md), [triagem neural](docs/AUDIO-TRIAGE.md) e [indicador circular](docs/VISUAL-MONITOR.md). A versão 0.1.6 foi validada com simulações; microfone pessoal, chave real, inferência paga, pesquisa remota e instalação por outro usuário não foram testados ao vivo.
