# Painel compacto — 0.1.20

O painel abre alinhado à direita do menu do indicador, 8 DIP abaixo dele, respeitando a área útil do monitor do indicador e seu DPI. Largura inicial de 420 DIP, redimensionável entre 340 e 520 DIP; altura inicial de 720 DIP e mínimo de 480 DIP, limitada à área disponível. A janela permanece acessível por Alt+Tab e mantém o fechamento sem interromper atividades.

A barra lateral foi substituída por cinco ícones SVG Lucide locais. A seção selecionada aparece no título; ícones têm dicas, nomes acessíveis e foco por teclado. Ações de monitoramento, gravação, cópia, reescrita, chave, catálogo e salvamento mantêm seus callbacks existentes. Configurações quebram textos longos e preservam o rodapé de salvar; editor e resultado aproveitam a altura disponível. A troca de página mantém os textos e históricos em memória.

Sem alteração de captura, APIs, modelos, armazenamento, credenciais ou configuração do usuário. Nenhuma migração, backup, rollback ou publicação remota foi criada.

Validação: suíte existente scripts/Test.ps1 -Ui (90 casos funcionais e 174 verificações WPF); inspeção manual em demonstração offline a 420 e 340 DIP, nas cinco seções, com rolagem das configurações, foco por Tab, reescrita simulada e consulta do resultado no histórico. Gravações e transcrições foram abertas com conteúdo de demonstração existente. Não foram criados testes ou fixtures. Microfone pessoal e inferência autenticada não foram usados. DPI alternativo e mudança física de monitor não foram exercitados manualmente nesta atualização.

Instalador local: artifacts/StudyWhisper-0.1.20-Setup-x64.exe.

Instalado localmente em 07/11/2026: versao 0.1.20.0, instalador concluido sem reinicializacao. Verify-Package.ps1 aprovou 3 verificacoes de dependencias, 2 de audio e 174 de interface. Os 14 arquivos de dados existentes permaneceram identicos por SHA-256, incluindo configuracoes, chave protegida e gravacoes. Conferencia adicional na instalacao final a 340 x 480 DIP: configuracoes e reescrita acessiveis com rolagem. Painel deixado aberto, monitoramento pausado. Sem bloqueios pendentes.
