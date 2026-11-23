namespace StudyWhisper.App;

public sealed class SettingsWindow : Window
{
    private sealed record SttOption(string Id, string Label, string? Benchmark = null)
    {
        public override string ToString() => Label;
    }

    private const string VoiceArena = "Voice Arena, conjunto privado de fala espontânea em PT-BR. Taxa de erro semântica (sWER).";
    private const string ArtificialAnalysis = "Artificial Analysis, índice geral de taxa de erro por palavra (AA-WER).";
    private static readonly SttOption[] RecommendedStt =
    [
        new("elevenlabs/scribe-v2-medical", "Scribe v2 Medical · 4,9% WER clínico (EN/FR/DE)",
            "MedDictate, avaliado pela ElevenLabs em ditado clínico em inglês, francês e alemão. Sem WER publicado para PT-BR."),
        new("microsoft/mai-transcribe-2", "MAI-Transcribe 2 · 5,89% sWER PT-BR", VoiceArena),
        new("mistralai/voxtral-mini-transcribe-3-0", "Voxtral Mini Transcribe 3 · 2,2% AA-WER geral", ArtificialAnalysis),
        new("mistralai/voxtral-small-transcribe-3-0", "Voxtral Small Transcribe 3 · 2,1% AA-WER geral", ArtificialAnalysis),
        new("elevenlabs/scribe-v2", "Scribe v2 · 6,43% sWER PT-BR", VoiceArena),
        new("google/gemini-3.5-transcribe", "Gemini 3.5 Transcribe · 6,18% sWER PT-BR", VoiceArena),
        new("openai/gpt-4o-transcribe", "GPT-4o Transcribe · 2,40% WER PT-BR", "Koedesk, fala lida do FLEURS PT-BR."),
        new("openai/whisper-large-v3-turbo", "Whisper Large V3 Turbo · 3,9% WER PT-BR", "Vozbench, fala lida do FLEURS PT-BR, com o provedor Groq."),
    ];

    private readonly Controller controller;
    private readonly ComboBox stt = new() { SelectedValuePath = nameof(SttOption.Id) };
    private readonly ComboBox jev=new(),answer=new();
    private readonly CheckBox memory=new(){Content="Usar os dez últimos pares de pergunta e resposta"};
    private readonly CheckBox startup=new(){Content="Iniciar com Windows (monitoramento pausado)"};
    private readonly CheckBox zdr=new(){Content="Exigir ZDR e negar coleta nos modelos (STT, JEV e resposta)"};
    private readonly CheckBox search=new(){Content="Permitir pesquisa online quando necessária (custo adicional)"};
    private readonly CheckBox reduceMotion=new(){Content="Reduzir movimento do indicador (respeita também a preferência do Windows)"};
    private readonly TextBox close=new(),vad=new(),maxAudio=new();
    private readonly PasswordBox secret=new(){MinHeight=34,Padding=new Thickness(8),MaxLength=256};
    private readonly TextBlock notice=Ui.Text("",13,Ui.Muted);
    private readonly TextBlock keyStatus=Ui.Text("",13,Ui.Muted);
    public TextBlock Usage {get;}=Ui.Text("",13,Ui.Muted);
    private readonly System.Windows.Threading.DispatcherTimer usageTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly CancellationTokenSource lifetime=new();
    private readonly Button save,validate,remove,refresh;
    private bool networking,hasKey,hasCandidate;
    private Model[] models=OpenRouter.BundledModels();
    public ScrollViewer Scroller { get; }
    public SettingsWindow(Controller controller,Action? closeView=null,bool embedded=false)
    {
        this.controller=controller;Ui.Style(this);
        Title="StudyWhisper • Configurações";Width=660;Height=740;MinWidth=580;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var dock=new DockPanel{Margin=new Thickness(embedded?0:22)};
        var footer=new DockPanel{LastChildFill=true,Margin=new Thickness(0,5,0,0)};
        save=Ui.Button("Salvar e pausar",Save);DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);
        if(!embedded){var closeButton=Ui.Button("Fechar",closeView??new Action(Close));DockPanel.SetDock(closeButton,Dock.Right);footer.Children.Add(closeButton);}
        notice.Margin=new Thickness(0,0,4,0);notice.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(notice);
        DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var stack=new StackPanel(); Scroller=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=stack};dock.Children.Add(Scroller);Content=dock;
        if(!embedded)stack.Children.Add(Ui.Text("StudyWhisper",21));
        stack.Children.Add(Ui.Text(controller.Demo?"Demonstração local • áudio sintético e respostas simuladas.":"Escolha Perguntas para ouvir perguntas de estudo ou Transcrição para guardar o texto da fala.",14,Ui.Muted));
        if(!controller.DependencyStatus.Available)
        {
            Section(stack,"Pré-requisito do filtro");var dependencyNotice=Ui.Text(controller.DependencyStatus.Message,13,Ui.Muted);stack.Children.Add(dependencyNotice);
            stack.Children.Add(Ui.Button("Verificar pré-requisito do filtro",()=>{if(!controller.CanChangeSettings){dependencyNotice.Text="Pare a transcrição antes de verificar o filtro.";return;}controller.CheckDependencies();dependencyNotice.Text=controller.DependencyStatus.Available?"Filtro local disponível. Monitoramento pausado; ative quando quiser.":controller.DependencyStatus.Message;}));
        }
        Section(stack,"Chave individual do OpenRouter");
        stack.Children.Add(Ui.Text("Cole sua própria chave. A validação consulta somente a autenticação; não gera uma resposta. A chave validada fica protegida pelo Windows neste usuário.",13,Ui.Muted));
        stack.Children.Add(secret);hasKey=controller.Store.HasKey;keyStatus.Text=hasKey?"Chave cadastrada neste app. O valor não é exibido.":"Nenhuma chave cadastrada.";stack.Children.Add(keyStatus);
        var keyButtons=new WrapPanel();
        validate=Ui.Button("Validar e guardar chave",()=>_=ValidateKey());keyButtons.Children.Add(validate);
        remove=Ui.Button("Remover chave",()=>
        {
            if(controller.Demo||!controller.CanChangeSettings)return;
            controller.Pause();controller.Store.RemoveKey();hasKey=false;secret.Clear();keyStatus.Text="Chave removida.";
            RefreshActions();
        });
        keyButtons.Children.Add(remove);stack.Children.Add(keyButtons);
        Section(stack,"Modelos e dados");
        Field(stack,"Transcrição (áudio → texto)",stt);Field(stack,"Classificação JEV (texto → decisão)",jev);Field(stack,"Resposta (texto → texto)",answer);
        refresh=Ui.Button("Atualizar catálogo público",()=>_=Refresh());
        refresh.HorizontalAlignment=HorizontalAlignment.Left;stack.Children.Add(refresh);
        stack.Children.Add(Ui.Text("Catálogo base: 03/10/2026; JEV 1.14 conferido em 15/10/2026. Atualize para conferir disponibilidade atual. O modo Perguntas também usa os modelos de classificação e resposta.",12,Ui.Muted));
        stack.Children.Add(zdr);stack.Children.Add(search);
        stack.Children.Add(Ui.Text("O modelo escolhe se precisa pesquisar. Até uma consulta Exa por resposta, com custo adicional. A busca envia consultas à Exa, cuja retenção não é coberta pelo ZDR do modelo. As duas opções são independentes.",12,Ui.Muted));
        stack.Children.Add(Ui.Text("Nos dois modos, o áudio segue ao serviço de transcrição. Em Perguntas, o JEV recebe o texto e decide quando chamar o modelo de resposta. ZDR exige rotas compatíveis e pode impedir chamadas. Consulte a política dos provedores.",13,Ui.Muted));
        Section(stack,"Sessão e exibição");stack.Children.Add(memory);stack.Children.Add(startup);Field(stack,"Fechar resposta após (5–300 segundos)",close);
        stack.Children.Add(Ui.Text("Perguntas, respostas e falas desse modo ficam na memória da sessão. O modo Transcrição salva o texto neste computador.",13,Ui.Muted));
        Section(stack,"Uso desta sessão");stack.Children.Add(Usage);Usage.Text=controller.Counters.Snapshot().Summary+"\n"+controller.UsageDiagnostic+"\n"+controller.CaptureDiagnostic;
        stack.Children.Add(Ui.Text("Contadores só em memória. Fala detectada é uma estimativa acústica. Envios contam tentativas HTTP, inclusive falhas; silêncio nas margens e sobreposição entram no tempo enviado. Descartes por fila cheia, expiração ou cancelamento não representam economia do filtro.",12,Ui.Muted));
        usageTimer.Tick+=(_,_)=>Usage.Text=controller.Counters.Snapshot().Summary+"\n"+controller.UsageDiagnostic+"\n"+controller.CaptureDiagnostic;Scroller.IsVisibleChanged+=(_,_)=>{if(Scroller.IsVisible)usageTimer.Start();else usageTimer.Stop();};
        Section(stack,"Áudio e limites");Field(stack,"Probabilidade de início (0,3-0,8; maior = menos sensível)",vad);Field(stack,"Máximo por segmento (3-30 segundos)",maxAudio);
        stack.Children.Add(Ui.Text("Silero filtra silêncio e parte dos ruídos localmente. Não distingue perguntas, TV ou falantes. Clique em Parar no painel ou no indicador para encerrar a escuta. Perguntas só são classificadas pelo JEV após a transcrição.",13,Ui.Muted));
        stack.Children.Add(Ui.Text("Não há teto interno de chamadas ou gasto. Enquanto o monitoramento estiver ativo, transcrição, JEV, resposta e pesquisa podem gerar cobrança conforme o uso. Limites e saldo do provedor continuam valendo; HTTP 429 respeita a espera informada. Pausar cancela o processamento. A fila mantém até duas falas por 30 s; os limites técnicos de áudio e resposta são preservados.",13,Ui.Muted));
        stack.Children.Insert(stack.Children.IndexOf(memory)+1,reduceMotion);
        var s=controller.Settings;memory.IsChecked=s.Memory;search.IsChecked=s.EnableWebSearch;startup.IsChecked=s.StartWithWindows;zdr.IsChecked=s.RequireZdr;reduceMotion.IsChecked=s.ReduceMotion;
        close.Text=s.CloseSeconds.ToString();vad.Text=s.SpeechProbability.ToString(System.Globalization.CultureInfo.CurrentCulture);maxAudio.Text=s.MaxAudioSeconds.ToString();
        secret.PasswordChanged+=(_,_)=>{using var value=secret.SecurePassword;hasCandidate=value.Length>0;RefreshActions();};
        Populate(s.SttModel,s.JevModel,s.AnswerModel);RefreshActions();
        Closed+=(_,_)=>{usageTimer.Stop();lifetime.Cancel();lifetime.Dispose();};
        foreach(var c in new[]{memory,startup,zdr,reduceMotion,search})
        {
            c.Margin=new Thickness(0,7,0,7);var label=(string)c.Content;System.Windows.Automation.AutomationProperties.SetName(c,label);if(embedded)c.Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap};c.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        }
        double modelLabelWidth = embedded ? 260 : 480;
        stt.ItemTemplate = ModelTemplate(nameof(SttOption.Label), modelLabelWidth, nameof(SttOption.Benchmark));
        jev.ItemTemplate = answer.ItemTemplate = ModelTemplate(nameof(Model.Id), modelLabelWidth);
        stt.DropDownOpened += (_, _) => _ = stt.Dispatcher.InvokeAsync(() =>
        {
            // WPF brings the current selection to the top when the popup opens.
            if (stt.IsDropDownOpen && stt.Template.FindName("DropDownScrollViewer", stt) is ScrollViewer dropdown)
                dropdown.ScrollToTop();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    public void RefreshActions()
    {
        var available=controller.CanChangeSettings&&!networking;
        save.IsEnabled=available;
        save.ToolTip=controller.RecordingActive?"Pare a transcrição para salvar as configurações.":"Salvar as alterações e desligar o microfone";
        ToolTipService.SetShowOnDisabled(save,true);
        validate.IsEnabled=available&&!controller.Demo&&hasCandidate;
        remove.IsEnabled=available&&!controller.Demo&&hasKey;
        refresh.IsEnabled=!controller.Demo&&!networking;
    }
    private static void Section(Panel p,string label) {p.Children.Add(new Border{Height=1,Background=Ui.Line,Margin=new Thickness(0,16,0,12)});p.Children.Add(Ui.Text(label,16));}
    private static void Field(Panel p,string label,Control control)
    {p.Children.Add(Ui.Text(label,13,Ui.Muted));control.MinHeight=34;control.Margin=new Thickness(0,0,0,12);control.Padding=new Thickness(8,5,8,5);System.Windows.Automation.AutomationProperties.SetName(control,label);p.Children.Add(control);}
    private static DataTemplate ModelTemplate(string labelPath, double maxWidth, string? benchmarkPath = null)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(labelPath));
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(FrameworkElement.MaxWidthProperty, maxWidth);
        if (benchmarkPath is not null)
            label.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(benchmarkPath));
        return new DataTemplate { VisualTree = label };
    }

    private void Populate(string sttId, string jevId, string answerId)
    {
        var availableStt = models.Where(model => model.IsStt).Select(model => model.Id).ToHashSet();
        var recommended = RecommendedStt.Where(option => availableStt.Contains(option.Id));
        var remaining = availableStt.Except(RecommendedStt.Select(option => option.Id))
            .OrderBy(id => id).Select(id => new SttOption(id, id));
        stt.ItemsSource = recommended.Concat(remaining).ToArray();
        stt.SelectedValue = sttId;

        foreach(var (box,items,value) in new[]{(jev,models.Where(x=>x.IsJev),jevId),(answer,models.Where(x=>x.IsChat),answerId)})
        {box.ItemsSource=items.OrderBy(x=>x.Id).ToArray();box.SelectedItem=items.FirstOrDefault(x=>x.Id==value);box.IsTextSearchEnabled=true;}
    }
    private async Task Refresh()
    {
        if(networking)return;networking=true;RefreshActions();notice.Text="Atualizando catálogo público…";
        try
        {
            var api=controller.ManagementApi();var all=await Task.WhenAll(api.ModelsAsync("transcription",lifetime.Token),api.ModelsAsync("decisions",lifetime.Token),api.ModelsAsync("text",lifetime.Token));
            var s=stt.SelectedValue as string??controller.Settings.SttModel;var j=(jev.SelectedItem as Model)?.Id??controller.Settings.JevModel;var a=(answer.SelectedItem as Model)?.Id??controller.Settings.AnswerModel;
            models=all.SelectMany(x=>x).DistinctBy(x=>x.Id).ToArray();Populate(s,j,a);notice.Text="Catálogo atualizado. Modelos retirados ficam sem seleção.";
        }
        catch(OperationCanceledException) {} catch(Exception ex) {notice.Text=ex is ApiException?ex.Message:"Não foi possível atualizar o catálogo. Verifique a conexão.";} finally {networking=false;RefreshActions();}
    }
    private async Task ValidateKey()
    {
        if(networking)return;if(!controller.CanChangeSettings){notice.Text="Pare a transcrição antes de alterar a chave.";return;}networking=true;RefreshActions();controller.Pause();var candidate=secret.Password.Trim();secret.Clear();keyStatus.Text="Validando…";
        try
        {await controller.ManagementApi().ValidateKeyAsync(candidate,lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();controller.Store.SaveKey(candidate);hasKey=true;keyStatus.Text="Chave validada e protegida pelo Windows. Monitoramento pausado.";}
        catch(OperationCanceledException) {} catch(Exception ex) {keyStatus.Text=ex is ApiException?ex.Message:"Não foi possível validar ou guardar a chave.";} finally {candidate="";networking=false;RefreshActions();}
    }
    private void Save()
    {
        if(!controller.CanChangeSettings){notice.Text="Pare a transcrição antes de salvar as configurações.";return;}
        try
        {
            var s=controller.Settings with {SttModel=stt.SelectedValue as string??"",JevModel=(jev.SelectedItem as Model)?.Id??"",AnswerModel=(answer.SelectedItem as Model)?.Id??"",Memory=memory.IsChecked==true,StartWithWindows=startup.IsChecked==true,RequireZdr=zdr.IsChecked==true,EnableWebSearch=search.IsChecked==true,
                ReduceMotion=reduceMotion.IsChecked==true,CloseSeconds=int.Parse(close.Text),SpeechProbability=double.Parse(vad.Text,System.Globalization.CultureInfo.CurrentCulture),MaxAudioSeconds=int.Parse(maxAudio.Text)};
            s.Validate();if(s.SttModel.Length==0)throw new ArgumentException("Selecione um modelo de transcrição.");
            controller.Save(s);notice.Text="Configurações salvas. Monitoramento pausado.";
        }
        catch(Exception ex) when(ex is FormatException or OverflowException or ArgumentException) {notice.Text=ex is ArgumentException?ex.Message:"Confira os valores numéricos.";}
        catch {notice.Text="Não foi possível salvar as configurações ou a inicialização com Windows.";}
    }
}
