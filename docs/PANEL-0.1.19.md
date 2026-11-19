# Painel navegável, 0.1.19

O botão superior do indicador, a opção Abrir painel e o atalho do aplicativo abrem a mesma janela. O menu de clique direito do indicador e da bandeja contém exatamente Abrir painel, Limpar memória e Sair. Os três botões inferiores preservam monitoramento, gravação e acesso à reescrita.

O painel reúne cinco seções:

- Em curso e sessão: etapa atual, controles de monitoramento e gravação, perguntas e respostas, fontes e cópia.
- Gravações: seleção das sessões salvas e em andamento, com filtros Tudo, Em curso e Histórico. Os textos finais, originais, cópia e reescrita continuam disponíveis.
- Reescritas: editor, histórico de textos avulsos da sessão e revisões das gravações. Trocar de seção não cancela a revisão nem apaga o texto digitado.
- Transcrições: textos reconhecidos e originais das gravações, além das transcrições do monitoramento da sessão, incluindo comentários reconhecidos pelo STT.
- Configurações: os controles existentes, incorporados à janela. Alterações que interromperiam uma gravação ou revisão ficam bloqueadas até sua conclusão.

Perguntas e respostas, transcrições do monitoramento e reescritas avulsas têm até 30 registros em memória; não são persistidas nem recuperadas de sessões anteriores. As gravações e suas revisões continuam no armazenamento local existente, sem migração. Limpar memória mantém sua função de limpar a conversa e agora também remove as transcrições de monitoramento da sessão; não apaga os arquivos das gravações.

O evento de transcrição usado pelo painel é local e separado dos eventos de diagnóstico, que continuam sem texto. A mudança não altera endpoints, modelos, captura de áudio nem as políticas de envio aos provedores.

Validação: suíte existente `scripts/Test.ps1 -Ui`, inspeção WPF em demonstração offline e `scripts/Verify-Package.ps1`. Não foram criados novos testes. A ação de limpeza não foi executada nos dados reais; os testes existentes usam seus controladores e diretórios isolados. Sem microfone pessoal ou inferência autenticada.

Instalador local: `artifacts/StudyWhisper-0.1.19-Setup-x64.exe`. Esta atualização não foi publicada.
