<!-- temporary-no-new-tests:begin -->
## Experimento temporário de testes

Desde 08/10/2026 até revisão explícita do usuário, não escrever novos testes nem ampliar cenários, snapshots, fixtures ou harnesses de teste, inclusive sob outro nome. Leia e aplique a [regra compartilhada](../TEMPORARY-NO-NEW-TESTS.md), que suspende somente exigências conflitantes de criar testes em contratos, skills, referências e perfil instalado. Preserve e execute as verificações/testes existentes; mantenha builds, typecheck, lint e inspeção funcional/manual apropriada, relatando falhas e limites. Não apagar testes, desabilitar CI nem adulterar critérios/asserções para obter aprovação. Todas as demais instruções e alterações preexistentes permanecem vigentes.
<!-- temporary-no-new-tests:end -->

# StudyWhisper

App Windows WPF, licença MIT. A única branch é `main`; não publicar sem pedido.
Leia `docs/ARCHITECTURE.md` e `docs/API.md` antes de alterar captura ou APIs.
Não acessar credenciais de outros apps, variáveis de ambiente com chaves, microfone pessoal ou inferência autenticada para testes. Use `--demo`, mocks e áudio sintético.
Execute `scripts/Test.ps1 -Ui`; `scripts/Build.ps1` gera pacote autossuficiente e instalador local. Mudanças no overlay exigem conferir foco e renderização real.
Não modificar outros repositórios, especialmente RN-Expander, nem documentos de snippets.
