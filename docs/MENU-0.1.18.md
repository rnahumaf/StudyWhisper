# Menu unificado, 0.1.18

O indicador e os três botões de ação agora compartilham uma única janela WPF, com fundo claro, borda sutil e cantos arredondados. O painel mede 104 × 104 DIP; o indicador mantém seus 72 × 72 DIP na parte superior. Monitoramento, Gravar/Parar e Rewrite continuam na linha inferior, com os mesmos callbacks, estados e nomes acessíveis.

O clique no indicador continua abrindo o histórico. O botão direito em qualquer área do painel abre o menu de ações existente, incluindo configurações, gravações, limpeza e saída. O painel mantém NOACTIVATE e acompanha mudanças de área útil e DPI usando seu tamanho completo. A janela separada dos três botões foi removida.

A atualização não altera captura, APIs, modelos, credenciais ou armazenamento das gravações. O componente indicador isolado conserva a geometria circular anterior; no aplicativo, os controles são incorporados ao painel quadrado. As notas anteriores sobre o overlay de 72 DIP descrevem as versões anteriores.

Validação local: `scripts/Test.ps1 -Ui`, com 90 testes de lógica, auditoria de áudio offline e a suíte WPF existente. A expectativa de tamanho da janela completa foi atualizada para 104 DIP; os demais cenários existentes foram preservados. A inspeção de `artifacts/ui-smoke/overlay-canonical.png` confirmou o painel único, os quatro alvos e a ausência de cortes. Sem novos testes, microfone pessoal ou inferência autenticada.

Pacote local: `artifacts/StudyWhisper-0.1.18-Setup-x64.exe`. A publicação no GitHub não faz parte desta alteração.
