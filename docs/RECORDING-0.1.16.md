# Captura e reescrita, preparação local 0.1.16

Revisão realizada em 04/11/2026 com Astra e raciocínio máximo. Código preparado localmente; sem instalação, commit, push ou publicação.

## Diagnóstico confirmado

- A gravação não chamava a reescrita: parar apenas encerrava STT e a reconciliação dos trechos.
- `ConsolidatedTranscript` parava na primeira borda sem alinhamento seguro. O restante permanecia em `DraftTranscript`; a janela enfatizava a parte consolidada, dando aparência de conclusão incompleta.
- Uma janela maior podia substituir a prévia só por cobrir mais áudio, mesmo omitindo palavras. Uma âncora temporal também podia descartar uma cauda divergente. Os originais eram arquivados, mas informações podiam sair do texto principal.
- O painel tinha 380 × 390 DIP, sem redimensionamento, com a área principal limitada a 150 DIP e rolagens aninhadas. O Rewrite separado limitava a entrada a 12.000 caracteres e compartilhava o prazo de 45 segundos.

## Comportamento implementado

Ao parar, STT termina os blocos pendentes antes da reescrita. O texto completo disponível passa por revisão genérica de pontuação, parágrafos e gramática, com instruções para preservar fatos, números, unidades, negações, incertezas e ordem. A revisão não responde às perguntas ditadas, não resume e não usa pesquisa online. O modelo de resposta configurado é mantido; as partes podem gerar cobrança normal na conta do provedor.

Textos longos são divididos em partes de até 6.000 bytes UTF-8, sem limite global de 12.000 caracteres. Há contexto adjacente, prazo de 180 segundos e limite de 12.000 tokens por parte. Somente o conjunto concluído é disponibilizado como final. Respostas cortadas, vazias, recusadas ou com alterações detectadas em algarismos, negações e marcadores de incerteza são rejeitadas com preservação do original. Essas guardas não comprovam fidelidade semântica.

A reconciliação só retira palavras quando sua correspondência foi preservada. Bordas divergentes permanecem identificadas; a reescrita não escolhe silenciosamente entre hipóteses. O journal conserva originais e texto final separadamente. Cancelar a reescrita ou recuperar uma sessão após encerramento não dispara novas chamadas nem publica um resultado parcial. Uma nova tentativa exige o comando no próprio painel.

A janela passa a 440 × 540 DIP, limitada à área disponível. Um botão expande para baixo; o puxador inferior ajusta altura por arraste ou setas do teclado. Original e final têm áreas flexíveis, divisor ajustável e cópia separada. Atualizações preservam seleção, posição de leitura e proporção do divisor. A área principal mostra também o conteúdo ainda não consolidado, sem ocultar a continuação após a primeira borda incerta.

## Validação

- `scripts/Test.ps1 -Ui`: 90/90 verificações de núcleo, auditoria de áudio offline e 174/174 verificações WPF; build sem erros ou avisos.
- Inspeção manual de APIs compiladas: um texto sintético com 17.901 caracteres foi dividido e recomposto sem perda de caracteres, incluindo o final.
- Journal sintético: original, transcrição e final preservados após releitura; números, data e negação mantidos.
- Prévia com negação e sinal numérico divergentes: reconciliação automática recusada.
- Cancelamento: captura continuou concluída, revisão ficou cancelada e nenhum final parcial foi publicado.
- Recuperação de revisão em andamento: captura permaneceu concluída; nenhuma inferência foi repetida.
- `finish_reason=length`: resultado parcial rejeitado.

Nenhum teste, cenário persistente, fixture ou harness novo foi criado. Nenhum microfone pessoal, dado clínico real, credencial ou inferência autenticada foi usado. As verificações existentes não cobrem integralmente a qualidade da nova reescrita remota.

A janela antiga foi observada na demonstração offline. A tentativa de inspeção visual manual da versão corrigida retornou uma imagem preta e `failed to activate captured window`, inclusive após recuperar a referência da janela. Os testes WPF existentes passaram, mas expansão por arraste, proporção dos painéis com texto longo e aparência final ainda precisam de conferência manual. A demonstração aberta para a revisão foi encerrada; nenhum servidor de teste foi iniciado.
