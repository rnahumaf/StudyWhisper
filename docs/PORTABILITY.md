# Portabilidade e pré-requisito nativo — 0.1.3

O instalador inclui .NET e ONNX Runtime, mas não distribui nem instala o Microsoft Visual C++ Redistributable. A versão 0.1.2 registrava o pré-requisito no README e retornava um erro genérico ao ativar; a 0.1.3 acrescenta uma verificação antes de qualquer captura ou leitura de chave, com aviso acionável no início do app.

## Verificação e recuperação

No primeiro início e antes de ativar, `NativeRuntime.Check` tenta carregar as quatro bibliotecas C++ importadas pelo ONNX Runtime 1.30.0 x64: `MSVCP140.dll`, `MSVCP140_1.dll`, `VCRUNTIME140.dll` e `VCRUNTIME140_1.dll`. A consulta usa caminhos absolutos da pasta do app e do diretório de sistema, libera cada referência obtida e não usa apenas presença de arquivo ou uma flag do registro como comprovação.

Bibliotecas carregáveis ainda podem ter uma versão incompatível. Por isso, o início do app cria o filtro ONNX, executa um bloco de silêncio sintético e zera/descarta o estado antes de continuar. A ativação também passa por essa inicialização, antes de ler DPAPI ou abrir WinMM. `DllNotFoundException`, `BadImageFormatException`, `EntryPointNotFoundException` e esses erros encapsulados são tratados como falha nativa; erros de modelo que não são do loader não são atribuídos automaticamente ao Visual C++.

Se uma biblioteca C++ não carrega, o aviso informa Microsoft Visual C++ v14 Redistributable **x64**, orienta instalar/reparar a versão atual e reabrir o StudyWhisper. Se o componente ONNX não carrega apesar da sondagem C++ ter passado, o aviso também orienta reinstalar o StudyWhisper se reparar o runtime não resolver. O texto não expõe a mensagem bruta da DLL e não afirma que toda falha ONNX é ausência de Visual C++.

O app permanece aberto e pausado. O aviso abaixo do círculo não rouba foco; contém **Página oficial da Microsoft (x64)** e **Fechar**. O navegador só é aberto por clique explícito nesse botão. Configurações repete o diagnóstico e oferece verificar novamente; essa verificação pausa o monitoramento. Se o navegador não abrir, o aviso mostra o endereço oficial. A demonstração offline continua disponível sem ONNX/Visual C++.

Não há download automático, execução de instalador, aceite de termos, mudança de registro, trust store ou certificados. Um Windows sem o runtime ainda pode instalar/abrir o app, mas o monitoramento fica indisponível até o usuário resolver o pré-requisito. O pacote é x64; ter apenas runtime x86 não basta.

## Evidência

- **62 testes** de núcleo/mocks passam, incluindo ausência total e parcial, erro de DLL, arquitetura errada, símbolo faltante, exceção encapsulada, preservação de erros não relacionados e uma sondagem positiva real neste PC.
- **21 verificações WPF** passam, incluindo aviso normal e com 360 DIP, foco preservado, ações alcançáveis, URI oficial entregue a callback de teste e fechamento sem ativar monitoramento.
- `--dependency-smoke --output <pasta>` simula ausência de dependência e falha encapsulada no próprio EXE, sem criar Controller, ler preferências/chave, abrir microfone ou usar HTTP.
- `--audio-smoke` confirma dependências e ONNX CPU reais disponíveis no PC de referência, com silêncio sintético. O teste de falta não renomeia/remove DLLs do sistema; é uma injeção explícita de falha, não uma instalação em Windows limpo.

Arquivos: `artifacts/0.1.3-test-results.txt`, `artifacts/0.1.3-build-results.txt`, `artifacts/ui-smoke-0.1.3`, `artifacts/package-dependency-smoke-0.1.3`, `artifacts/package-audio-smoke-0.1.3`, `artifacts/package-ui-smoke-0.1.3`. A lista de imports PE e o hash da DLL oficial estão em `artifacts/0.1.3-onnx-native-imports.json`.

Pendente: instalação em máquina limpa/VM sem runtime, runtimes antigos reais e navegação no browser do usuário. Nenhum runtime ou app foi instalado para os testes, e a versão em uso foi preservada. O evento anterior de inicialização do SDK/certificado permanece registrado em `VERIFICATION.md`; a prevenção nos scripts foi mantida, sem consultar ou alterar certificados/trust store.

Fontes: [requisitos oficiais ONNX Runtime](https://onnxruntime.ai/docs/install/), [Visual C++ Redistributable oficial, arquiteturas e compatibilidade](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170), [formato PE Microsoft para inspeção das importações](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
