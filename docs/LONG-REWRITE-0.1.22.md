# Reescrita longa — 0.1.22

## Causa confirmada

A imagem enviada mostra o aviso do antigo `RewriteAsync` para `finish_reason=length`: a resposta atingiu o teto de saída. A reescrita natural enviava todo o texto em uma única chamada, com 6.000 tokens de saída e limite local de 12.000 caracteres. O caminho separado de revisão de gravações já dividia o texto, mas descartava o progresso de todas as partes quando uma delas falhava.

## Comportamento corrigido

- Os dois caminhos usam o mesmo processamento sequencial, sem teto de tamanho total no aplicativo. Cada parte tem até 3.000 bytes UTF-8, preferindo finais de frases/parágrafos, sem partir pares surrogate. Cada caractere pertence a uma única parte; o contexto adjacente é somente para leitura.
- A reescrita natural mantém integralmente o prompt canônico LMDocs. O contrato adicional pede apenas a reescrita da parte, sem resumo ou repetição do contexto.
- Cada requisição tem limite de saída de 12.000 tokens e prazo de três minutos. `finish_reason=length` subdivide somente a parte afetada; subdivisões param em trechos pequenos para impedir repetição infinita. Respostas vazias, recusas e outras finalizações não são aceitas como conclusão.
- A junção ocorre localmente, na ordem original, preservando os separadores que pertencem a cada trecho. Não há uma chamada final com o documento inteiro, que reintroduziria o limite.
- Partes aceitas e decisões de subdivisão são gravadas incrementalmente em `LocalAppData/StudyWhisper/rewrite-work`. São registros de progresso, não backups. A chave depende do texto completo, modelo, política de dados e instruções; alterações nesses elementos não misturam trabalhos incompatíveis.
- Reenviar o mesmo texto com o mesmo modelo retoma as partes concluídas, inclusive após reiniciar o app. Falhas de rede, saldo, autenticação e HTTP 429 param o trabalho; não disparam tentativas pagas automáticas. O cooldown existente permanece vigente. Apenas truncamento de saída provoca subdivisão automática.
- Cancelamento mantém original e partes aceitas; uma resposta recebida depois do cancelamento não é aceita. Uma linha final interrompida no registro não invalida as anteriores. Se o disco falhar, o trabalho para antes de avançar; o resultado ainda em memória pode ser salvo numa nova tentativa enquanto o app permanecer aberto.
- O resultado final só substitui a visualização depois da conclusão de todas as partes. Originais, gravações existentes e resultados anteriores permanecem preservados. A interface mostra progresso percentual e oferece retomada no botão de revisão de gravações interrompidas.
- As verificações literais de números, negações e marcadores de incerteza agora também protegem a reescrita natural. Elas não constituem prova de fidelidade semântica de um modelo de linguagem.

## Evidência de validação

Sem novos arquivos/casos de testes, fixtures ou harnesses. Foram executadas as suítes existentes e chamadas manuais diretas às APIs compiladas, com delegates locais e o `MockHttp` já existente. Nenhuma chamada autenticada, microfone pessoal ou dado real foi enviado.

- 90 testes funcionais e 174 verificações WPF existentes passaram.
- Identidade integral de entrada/saída com respostas eco locais para 2.999, 3.000, 3.001, 11.999, 12.000, 12.001 e 250.001 caracteres, incluindo texto contínuo sem espaços.
- 168.000 caracteres com acentos, emoji, caracteres chineses, números e CRLF: igualdade exata.
- Falha na terceira parte de 100.003 caracteres: retomada começou no offset 6.000; saída final exatamente igual à entrada.
- Truncamentos sintéticos acima de 700 caracteres: subdivisão terminou com saída integral; reabertura do armazenamento retomou tudo sem novas chamadas.
- Cancelamento durante a segunda parte: propagado; retomada começou no offset 3.000 e preservou exatamente o conteúdo.
- Caminho HTTP de reescrita natural: 15.000 caracteres em cinco chamadas, saída integral; nova instância do adaptador/armazenamento reaproveitou as cinco partes sem chamada adicional. Revisão de gravação também retornou saída integral.
- Truncamento persistente foi rejeitado após três tentativas finitas no exemplo de 600 caracteres. Alteração sintética de `20 mg` para `10 mg` foi rejeitada.

O transporte mantém o contrato oficial de [Chat Completions do OpenRouter](https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion), consultado em 07/11/2026. A escala prática depende de memória/disco, disponibilidade e capacidade do modelo escolhido; remover o teto local não elimina falhas externas nem garante a fidelidade semântica do provedor.

## Pacote instalado e interface

O editor foi corrigido para rolar textos longos dentro de campos com altura limitada, mantendo a acao de reescrita visivel. A demonstracao WPF concluiu um texto sintetico acima de 25.000 caracteres com o botao acessivel e campos de entrada/saida separados.

O pacote final passou em 179 verificacoes: 3 de dependencias, 2 de audio e 174 WPF. A verificacao de foco falhou enquanto outras instancias estavam abertas e passou na execucao isolada, sem alterar testes ou criterios. Instalador concluido com codigo 0, versao 0.1.22.0, sem reiniciar o Windows. O SHA-256 do executavel instalado corresponde ao pacote validado. Os 15 arquivos existentes de dados/configuracoes mantiveram seus hashes. Aplicativo instalado aberto e visivel, com monitoramento desativado, pronto para teste de Rodrigo. Nenhum backup/rollback criado; nenhum bloqueio pendente.

Logs locais: artifacts/long-rewrite-0.1.22-tests.log, artifacts/long-rewrite-0.1.22-build.log, artifacts/long-rewrite-0.1.22-package.log e artifacts/long-rewrite-0.1.22-install.log.
