# Verificação 0.1.7, 04/10/2026

Patch para liberar pesquisa Exa com ZDR no modelo. A 0.1.6 bloqueava a ferramenta por decisão conservadora incorreta da implementação; o OpenRouter admite essa combinação, com retenção independente no buscador. Não houve mudança em configurações pessoais ou relaxamento de `provider.zdr=true` e `data_collection=deny`.

90 testes de núcleo/mocks verificam Exa habilitado com ZDR, opt-out com ZDR ligado/desligado, flags em STT/JEV/resposta, instruções de consulta mínima, contexto separado da definição da ferramenta, fontes e preservação das duas preferências. 74 verificações WPF incluem as anteriores e a seleção independente de pesquisa/ZDR com explicação de retenção. Captura nativa: `settings-search-zdr.png`.

`scripts/Test.ps1 -Ui`, `scripts/Build.ps1` e `scripts/Verify-Package.ps1` são os comandos de validação. Evidências: `artifacts/0.1.7-test-results.txt`, `artifacts/ui-smoke-0.1.7`, `artifacts/0.1.7-build-results.txt`, `artifacts/0.1.7-package-checks.json` e `artifacts/package-ui-smoke-0.1.7`.

Testes usam mocks HTTP, chaves fictícias e áudio sintético. Não houve busca/inferência paga, uso de microfone pessoal, leitura de chave pessoal, instalação, mudança de preferências ou reinício do app em uso. O prompt orienta minimização, mas o app não intercepta a consulta do server tool para validá-la mecanicamente. Não há promessa de anonimização infalível, retenção zero da Exa ou validação ao vivo do serviço remoto. O instalador permanece sem assinatura digital.
