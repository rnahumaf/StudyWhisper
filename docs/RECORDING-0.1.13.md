# Gravação com transcrição ao vivo — versão local 0.1.13

No menu do círculo ou da bandeja, **Gravar** troca a captura habitual pela transcrição contínua. **Parar** fecha a entrada, envia o trecho final e aguarda a fila. Ao terminar, o app retorna ao modo habitual no estado anterior: ativo se estava ativo, pausado se estava pausado. Configurações, histórico de perguntas/respostas, chave, modelos, fonte de áudio e política de dados são preservados. Não há início automático de gravação, inclusive após reiniciar.

**Gravações** abre a lista persistente e permite selecionar e copiar a transcrição. A janela mostra Gravando, Parando, Concluído ou Interrompido. O ponto vermelho no círculo e o menu indicam a sessão ativa. Clicar fora fecha a janela e os menus, sem parar a gravação. A consulta pode ser reaberta. O histórico habitual continua separado e seu comando Limpar não apaga gravações.

## Captura e processamento

É usada a mesma captura `Microphone`/WinMM e o dispositivo padrão de gravação já utilizado pelo app. Não há fonte adicional, permissões novas ou outro provedor. A captura não passa pelo VAD, JEV, espera por perguntas ou fila habitual: todos os quadros recebidos, incluindo silêncio e fala baixa, entram em janelas consecutivas de 5 segundos, sem sobreposição. Apenas STT é chamado, com o modelo e a política de provedor já configurados. Há latência do bloco e do serviço; a qualidade das fronteiras de frases depende do STT. Não há promessa de precisão literal do provedor nem transcrição instantânea por palavra.

A troca aguarda a conclusão da tarefa habitual cancelada, inclusive se uma implementação ignorar o cancelamento. Há uma única captura e uma chamada de transcrição ativa por vez. Parar aguarda as chamadas já enfileiradas antes de restaurar o pipeline habitual. O último bloco parcial também é enviado. Texto de segmentos distintos pode conter frases iguais; elas são preservadas, pois podem ser repetições reais. Cada janela tem um identificador crescente e seu resultado é aplicado uma só vez.

Não há expiração nem substituição silenciosa dos blocos da gravação. A fila em memória admite até 24 blocos pendentes (120 segundos), além da chamada ativa e do bloco em formação. Se alcançar a capacidade, a captura é interrompida e a lacuna é registrada explicitamente; os blocos anteriores são concluídos em ordem. Falhas de STT, inclusive cooldown do provedor, não repetem chamadas automaticamente. A gravação para, preserva os resultados disponíveis, registra o trecho sem transcrição e conclui o trabalho anterior pendente. Chamadas podem gerar custos de STT segundo o provedor; JEV, resposta e pesquisa não são chamados no modo de gravação.

## Persistência e falhas

Somente texto e metadados são gravados em `%LOCALAPPDATA%\StudyWhisper\recordings\<sessão>.jsonl`. O registro incremental inclui início, modelo STT, sequência, posições relativas do áudio recebido, texto confirmado, lacunas e estado. Cada registro usa flush em disco antes da publicação normal do resultado. Não há áudio bruto, base64, chave ou resposta remota de erro nesses arquivos. Os arquivos de transcrição são locais em texto legível e permanecem até sua remoção pelo usuário.

O áudio fica temporariamente em RAM e é limpo após consumo/encerramento. Não é mantido para retomada ou repetição remota. Assim, um encerramento inesperado pode perder áudio ainda não transcrito, mas mantém o texto já confirmado. Ao iniciar, sessões incompletas passam a Interrompido; blocos pendentes recebem uma nota de indisponibilidade. Uma última linha incompleta é truncada na recuperação, preservando os registros completos anteriores. A captura não é retomada.

Falhas da entrada continuam usando a recuperação local existente. O bloco parcial é fechado e a lacuna é indicada, com duração desconhecida. As posições em segundos representam amostras recebidas, sem inventar duração para períodos sem quadros. Uma sessão com lacunas termina Interrompido. Ao pausar explicitamente, sair ou ocorrer falha geral do app, texto confirmado fica preservado e o áudio pendente é descartado com indicação de interrupção.

Se o armazenamento ficar indisponível durante uma chamada, o texto retornado permanece em memória e acessível para copiar enquanto o app estiver aberto, com aviso explícito. A captura para. A persistência de texto não pode ser garantida enquanto o sistema negar gravação em disco.

## Entrega e instalação

O pacote 0.1.13 é uma preparação local. A instalação deve ocorrer somente depois da revisão e autorização do usuário, com o app atual parado em momento escolhido por ele. O instalador usa o caminho habitual por usuário e preserva preferências/chave; não há publicação nem instalação automática nesta entrega. O início normal continua pausado.
