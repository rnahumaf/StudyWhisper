> Histórico: esta proposta não foi implementada. A decisão para 0.1.2 foi manter Whisper V3 Turbo remoto e acrescentar triagem Silero local; veja [AUDIO-TRIAGE.md](AUDIO-TRIAGE.md).

# Transcrição local opcional — proposta, ainda não implementada

Para preservar a escuta natural e evitar o custo do STT remoto, o fluxo proposto é: VAD local de fala → Whisper multilíngue local → JEV textual → resposta pelo OpenRouter. JEV e respostas continuam pagos conforme o uso. Não foram baixados modelos nem instalados serviços; a atualização visual mantém o fluxo atual.

Silero VAD, com licença MIT e formato ONNX, é uma opção para distinguir melhor fala de ruído. Seu exemplo admite áudio a 8/16 kHz e uso por C#. Continua reconhecendo fala da TV ou de outras pessoas e não identifica perguntas. Somente aumentar o limiar de volume não resolve essa limitação. Fonte: [Silero VAD](https://github.com/snakers4/silero-vad).

Whisper.net integra whisper.cpp ao .NET, com opções de runtime para Windows. Deve usar um modelo multilíngue, mantido carregado durante a sessão. A máquina e o backend determinam a latência; não há medição de CPU/GPU neste app. Licenças e requisitos do pacote/modelo escolhido precisam ser mantidos no artefato. Fontes: [Whisper.net](https://github.com/sandrohanea/whisper.net), [whisper.cpp](https://github.com/ggml-org/whisper.cpp).

O README atual de Whisper.net 1.9.1 lista Windows 11/Server 2022, Visual C++ Redistributable 2022 e instruções AVX/AVX2/FMA/F16C para seu runtime de CPU padrão, além de uma variante NoAvx. CUDA tem requisitos próprios. O suporte do MVP a Windows 10 não deve ser automaticamente atribuído a esse modo futuro; a combinação escolhida precisa de avaliação antes de baixar ou empacotar dependências.

## Encaixe no código

O ponto de entrada de VAD é `Controller.OnFrame`; um detector de fala pode processar os quadros localmente antes de montar os mesmos WAVs limitados. `IStudyApi.TranscribeAsync` já é a fronteira para trocar STT remoto por local. Um adaptador de STT local pode implementar a transcrição e delegar somente `ClassifyAsync` e `AnswerAsync` ao adaptador OpenRouter. Isso preserva o pipeline de cancelamento, histórico e decisão.

Configurações deverá expor modos distintos, com caminho do modelo e backend. Escolher local deve exigir modelo válido e não acionar fallback cloud silencioso. Falha de carga ou processamento deve pausar ou apresentar erro. O carregamento do modelo deve ser persistente, liberado no encerramento; cancelar precisa impedir resultado tardio e interromper processamento quando o backend permitir.

O orçamento de inferências remotas deve continuar compartilhado entre JEV e resposta; STT local não chama `CallBudget.Take`. Um limite de minutos exportados e contadores visíveis por etapa seriam controles úteis mesmo no modo atual, mas não foram acrescentados sem uma decisão de produto.

## Aceite antes de substituir o modo ativo

Usar gravações públicas/licenciadas ou fixtures escolhidas para comparar fala, silêncio, ruído, TV, comentários e perguntas em PT-BR. Medir taxa de falsos disparos, latência, RAM/VRAM, cancelamento e ausência de conexões de STT remoto no modo local. Validar binários e licenças dos runtimes. Não medir com o microfone pessoal nem procurar chaves existentes.

Palavra de ativação ou botão pressionado para falar é uma alternativa mais previsível para reduzir os envios, mas altera a escuta natural. Deve ser escolhida pelo usuário. VAD neural pode reduzir ruído; não deve ser apresentado como detector local de intenção.
