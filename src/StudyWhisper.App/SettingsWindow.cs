namespace StudyWhisper.App;

public sealed class SettingsWindow : Window
{
    private readonly Controller controller;
    private readonly ComboBox stt=new(),jev=new(),answer=new();
    private readonly CheckBox memory=new(){Content="Usar os dez últimos pares de pergunta e resposta"};
    private readonly CheckBox startup=new(){Content="Iniciar com Windows (monitoramento pausado)"};
    private readonly CheckBox zdr=new(){Content="Exigir ZDR e negar coleta nos modelos (STT, JEV e resposta)"};
    private readonly CheckBox search=new(){Content="Permitir pesquisa online quando necessária (custo adicional)"};
    private readonly CheckBox reduceMotion=new(){Content="Reduzir movimento do indicador (respeita também a preferência do Windows)"};
    private readonly TextBox close=new(),minute=new(),session=new(),vad=new(),maxAudio=new();
    private readonly PasswordBox secret=new(){MinHeight=34,Padding=new Thickness(8),MaxLength=256};
    private readonly TextBlock notice=Ui.Text("",13,Ui.Muted);
    private readonly TextBlock keyStatus=Ui.Text("",13,Ui.Muted);
    public TextBlock Usage {get;}=Ui.Text("",13,Ui.Muted);
    private readonly System.Windows.Threading.DispatcherTimer usageTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly CancellationTokenSource lifetime=new();
    private bool networking;
    private Model[] models=OpenRouter.BundledModels();
    public ScrollViewer Scroller { get; }
    public SettingsWindow(Controller controller)
    {
        this.controller=controller;Ui.Style(this);
        Title="StudyWhisper • Configurações";Width=660;Height=740;MinWidth=580;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var dock=new DockPanel{Margin=new Thickness(22)};
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        footer.Children.Add(Ui.Button("Fechar",Close));footer.Children.Add(Ui.Button("Salvar e pausar",Save));
        DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var stack=new StackPanel(); Scroller=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=stack};dock.Children.Add(Scroller);Content=dock;
        stack.Children.Add(Ui.Text("StudyWhisper",21));
        stack.Children.Add(Ui.Text(controller.Demo?"Demonstração local • áudio sintético e respostas simuladas.":"Escuta para estudo, respostas curtas em texto. Ative o monitoramento pelo menu da bandeja.",14,Ui.Muted));
        if(!controller.DependencyStatus.Available)
        {
            Section(stack,"Pré-requisito do filtro");var dependencyNotice=Ui.Text(controller.DependencyStatus.Message,13,Ui.Muted);stack.Children.Add(dependencyNotice);
            stack.Children.Add(Ui.Button("Verificar pré-requisito do filtro",()=>{controller.CheckDependencies();dependencyNotice.Text=controller.DependencyStatus.Available?"Filtro local disponível. Monitoramento pausado; ative quando quiser.":controller.DependencyStatus.Message;}));
        }
        Section(stack,"Chave individual do OpenRouter");
        stack.Children.Add(Ui.Text("Cole sua própria chave. A validação consulta somente a autenticação; não gera uma resposta. A chave validada fica protegida pelo Windows neste usuário.",13,Ui.Muted));
        stack.Children.Add(secret);keyStatus.Text=controller.Store.HasKey?"Chave cadastrada neste app. O valor não é exibido.":"Nenhuma chave cadastrada.";stack.Children.Add(keyStatus);
        var keyButtons=new WrapPanel();
        var validate=Ui.Button("Validar e guardar chave",()=>_=ValidateKey());validate.IsEnabled=!controller.Demo;keyButtons.Children.Add(validate);
        var remove=Ui.Button("Remover chave",()=>{if(controller.Demo)return;controller.Pause();controller.Store.RemoveKey();secret.Clear();keyStatus.Text="Chave removida. Monitoramento pausado.";});remove.IsEnabled=!controller.Demo;keyButtons.Children.Add(remove);stack.Children.Add(keyButtons);
        Section(stack,"Modelos e dados");
        Field(stack,"Transcrição (áudio → texto)",stt);Field(stack,"Classificação JEV (texto → decisão)",jev);Field(stack,"Resposta (texto → texto)",answer);
        var refresh=Ui.Button("Atualizar catálogo público",()=>_=Refresh());refresh.IsEnabled=!controller.Demo;stack.Children.Add(refresh);
        stack.Children.Add(Ui.Text("Catálogo incluído: 03/10/2026. Atualize para conferir disponibilidade atual. Escolha o modelo de resposta antes de ativar.",12,Ui.Muted));
        stack.Children.Add(zdr);stack.Children.Add(search);
        stack.Children.Add(Ui.Text("O modelo escolhe se precisa pesquisar. Até uma consulta Exa por resposta, com custo adicional. A busca envia consultas à Exa, cuja retenção não é coberta pelo ZDR do modelo. As duas opções são independentes.",12,Ui.Muted));
        stack.Children.Add(Ui.Text("Ao ativar, segmentos de áudio vão ao OpenRouter/STT; transcrição e contexto vão ao JEV e ao modelo de resposta. ZDR exige rotas compatíveis e pode impedir chamadas. Consulte a política dos provedores.",13,Ui.Muted));
        Section(stack,"Sessão e exibição");stack.Children.Add(memory);stack.Children.Add(startup);Field(stack,"Fechar resposta após (5–300 segundos)",close);
        stack.Children.Add(Ui.Text("Memória e histórico ficam apenas em RAM; limpar cancela o processamento atual. O popover preserva sua posição enquanto você lê.",13,Ui.Muted));
        Section(stack,"Uso desta sessão");stack.Children.Add(Usage);Usage.Text=controller.Counters.Snapshot().Summary+"\n"+controller.BudgetDiagnostic;
        stack.Children.Add(Ui.Text("Contadores só em memória. Fala detectada é uma estimativa acústica. Envios contam tentativas HTTP, inclusive falhas; silêncio nas margens e sobreposição entram no tempo enviado. Descartes por fila cheia, expiração ou cancelamento não representam economia do filtro.",12,Ui.Muted));
        usageTimer.Tick+=(_,_)=>Usage.Text=controller.Counters.Snapshot().Summary+"\n"+controller.BudgetDiagnostic;usageTimer.Start();
        Section(stack,"Áudio e limites");Field(stack,"Probabilidade de início (0,3-0,8; maior = menos sensível)",vad);Field(stack,"Máximo por segmento (3-30 segundos)",maxAudio);Field(stack,"Máximo de chamadas por minuto (3-60)",minute);Field(stack,"Máximo de chamadas por sessão (3-3000)",session);
        stack.Children.Add(Ui.Text("Silero filtra silêncio e parte dos ruídos localmente. Não distingue perguntas, TV ou falantes. Em ambiente ruidoso, pause pela bandeja. Perguntas só são classificadas pelo JEV após a transcrição.",13,Ui.Muted));
        stack.Children.Add(Ui.Text("Até três chamadas HTTP por pergunta, além de uma busca opcional. Falas ignoradas pelo JEV usam STT e classificação e também contam no limite. Ao atingir o limite, aguarde o tempo indicado; repetir a pergunta não acelera a recuperação. A fila guarda até duas falas por 30 s; excesso substitui a pendente mais antiga. Pausar/limpar apaga a fila. Sem repetição automática. O limite total só reinicia ao sair do app.",13,Ui.Muted));stack.Children.Add(notice);
        stack.Children.Insert(stack.Children.IndexOf(memory)+1,reduceMotion);
        var s=controller.Settings;memory.IsChecked=s.Memory;search.IsChecked=s.EnableWebSearch;startup.IsChecked=s.StartWithWindows;zdr.IsChecked=s.RequireZdr;reduceMotion.IsChecked=s.ReduceMotion;
        close.Text=s.CloseSeconds.ToString();minute.Text=s.CallsPerMinute.ToString();session.Text=s.SessionCallLimit.ToString();vad.Text=s.SpeechProbability.ToString(System.Globalization.CultureInfo.CurrentCulture);maxAudio.Text=s.MaxAudioSeconds.ToString();
        Populate(s.SttModel,s.JevModel,s.AnswerModel);
        Closed+=(_,_)=>{usageTimer.Stop();lifetime.Cancel();lifetime.Dispose();};
        foreach(var c in new[]{memory,startup,zdr,reduceMotion})c.Margin=new Thickness(0,7,0,7);
    }
    private static void Section(Panel p,string label) {p.Children.Add(new Border{Height=1,Background=Ui.Line,Margin=new Thickness(0,16,0,12)});p.Children.Add(Ui.Text(label,16));}
    private static void Field(Panel p,string label,Control control)
    {p.Children.Add(Ui.Text(label,13,Ui.Muted));control.MinHeight=34;control.Margin=new Thickness(0,0,0,12);control.Padding=new Thickness(8,5,8,5);System.Windows.Automation.AutomationProperties.SetName(control,label);p.Children.Add(control);}
    private void Populate(string s,string j,string a)
    {
        foreach(var (box,items,value) in new[]{(stt,models.Where(x=>x.IsStt),s),(jev,models.Where(x=>x.IsJev),j),(answer,models.Where(x=>x.IsChat),a)})
        {box.ItemsSource=items.OrderBy(x=>x.Id).ToArray();box.SelectedItem=items.FirstOrDefault(x=>x.Id==value);box.IsTextSearchEnabled=true;}
    }
    private async Task Refresh()
    {
        if(networking)return;networking=true;notice.Text="Atualizando catálogo público…";
        try
        {
            var api=controller.ManagementApi();var all=await Task.WhenAll(api.ModelsAsync("transcription",lifetime.Token),api.ModelsAsync("decisions",lifetime.Token),api.ModelsAsync("text",lifetime.Token));
            var s=(stt.SelectedItem as Model)?.Id??controller.Settings.SttModel;var j=(jev.SelectedItem as Model)?.Id??controller.Settings.JevModel;var a=(answer.SelectedItem as Model)?.Id??controller.Settings.AnswerModel;
            models=all.SelectMany(x=>x).DistinctBy(x=>x.Id).ToArray();Populate(s,j,a);notice.Text="Catálogo atualizado. Modelos retirados ficam sem seleção.";
        }
        catch(OperationCanceledException) {} catch(Exception ex) {notice.Text=ex is ApiException?ex.Message:"Não foi possível atualizar o catálogo. Verifique a conexão.";} finally {networking=false;}
    }
    private async Task ValidateKey()
    {
        if(networking)return;networking=true;controller.Pause();var candidate=secret.Password.Trim();secret.Clear();keyStatus.Text="Validando…";
        try
        {await controller.ManagementApi().ValidateKeyAsync(candidate,lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();controller.Store.SaveKey(candidate);keyStatus.Text="Chave validada e protegida pelo Windows. Monitoramento pausado.";}
        catch(OperationCanceledException) {} catch(Exception ex) {keyStatus.Text=ex is ApiException?ex.Message:"Não foi possível validar ou guardar a chave.";} finally {candidate="";networking=false;}
    }
    private void Save()
    {
        try
        {
            var s=controller.Settings with {SttModel=(stt.SelectedItem as Model)?.Id??"",JevModel=(jev.SelectedItem as Model)?.Id??"",AnswerModel=(answer.SelectedItem as Model)?.Id??"",Memory=memory.IsChecked==true,StartWithWindows=startup.IsChecked==true,RequireZdr=zdr.IsChecked==true,EnableWebSearch=search.IsChecked==true,
                ReduceMotion=reduceMotion.IsChecked==true,CloseSeconds=int.Parse(close.Text),CallsPerMinute=int.Parse(minute.Text),SessionCallLimit=int.Parse(session.Text),SpeechProbability=double.Parse(vad.Text,System.Globalization.CultureInfo.CurrentCulture),MaxAudioSeconds=int.Parse(maxAudio.Text)};
            s.Validate();if(s.SttModel.Length==0||s.JevModel.Length==0||s.AnswerModel.Length==0)throw new ArgumentException("Selecione os três modelos do catálogo.");
            controller.Save(s);notice.Text="Configurações salvas. Monitoramento pausado.";
        }
        catch(Exception ex) when(ex is FormatException or OverflowException or ArgumentException) {notice.Text=ex is ArgumentException?ex.Message:"Confira os valores numéricos.";}
        catch {notice.Text="Não foi possível salvar as configurações ou a inicialização com Windows.";}
    }
}
