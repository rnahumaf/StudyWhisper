# Leitura compacta — 0.1.21

Todo texto de controles, transcrições, reescritas, respostas, menus e dicas usa 11 DIP (equivalente WPF a 11 CSS px, respeitando a escala do Windows); títulos usam 14 DIP. A navegação tem apenas um sublinhado discreto, os botões SVG perderam caixas permanentes e o cabeçalho reúne as ações. O estado detalhado continua na dica do título e os avisos relevantes permanecem disponíveis.

Transcrições e gravações exibem uma única área de texto. A área “Original · sem reescrita” e o divisor foram retirados; os originais e as revisões continuam armazenados internamente. Marcadores gerados de borda/provisório e intervalos pendentes deixam de aparecer no texto visível. Lacunas reais continuam indicadas como “Trecho indisponível”; os detalhes permanecem no journal. Nenhuma palavra transcrita é descartada para deduplicação por suposição.

O botão de lápis “Reescrita natural” está nas transcrições de gravações e de monitoramento. Ele envia o texto para o fluxo canônico de reescrita natural e abre o resultado em Reescritas > Histórico, sem substituir o rascunho do editor. Há cópia e cancelamento; resultados ficam no histórico de sessão existente. O limite de 12.000 caracteres desse fluxo é preservado, com erro visível e texto de origem intacto quando excedido. O botão circular “Revisar gravação salva” mantém a revisão persistente anterior para gravações completas.

Dados, chave, modelos, políticas de rede, captura e algoritmos de consolidação permanecem inalterados. A remoção dos marcadores é de apresentação, não uma alegação de correção automática de erros de transcrição.

Validação: scripts/Test.ps1 -Ui, inspeção WPF real em demonstração offline a 420 x 720 e 340 x 480 DIP, seleção de gravação, ausência da área duplicada, envio de transcrição à reescrita natural e resultado no histórico. Sem novos testes/harnesses, microfone pessoal ou inferência autenticada. Pacote e instalação registrados após a validação final.

Validacao final em 2026-11-07: 90 testes funcionais e 174 verificacoes WPF passaram. O pacote final passou em 179 verificacoes (3 dependencias, 2 audio, 174 interface). Build sem erros e sem avisos. Instalador executado com sucesso (codigo 0), sem reinicializacao. Versao instalada 0.1.21.0; SHA-256 do executavel identico ao pacote validado. Os 15 arquivos de dados/configuracoes existentes mantiveram seus hashes apos a atualizacao. A interface instalada foi conferida visualmente e ficou aberta com monitoramento desativado. Nenhum backup ou rollback criado.
