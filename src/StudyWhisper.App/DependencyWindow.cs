using System.Diagnostics;
namespace StudyWhisper.App;
/// <summary>Passive notice; browser navigation occurs only on an explicit button click.</summary>
public sealed class DependencyWindow:PassiveWindow
{
    public TextBlock Explanation {get;}
    public Button HelpButton {get;}
    public Button CloseButton {get;}
    public DependencyWindow(NativeReadiness problem,Action<Uri>? open=null)
    {
        Title="StudyWhisper · Pré-requisito do filtro";Width=440;SizeToContent=SizeToContent.Height;
        MaxHeight=Math.Min(390,SystemParameters.WorkArea.Height-24);
        var stack=new StackPanel{Margin=new Thickness(18)};
        stack.Children.Add(Ui.Text("Pré-requisito do filtro de áudio",17));
        Explanation=Ui.Text(problem.Message,13);stack.Children.Add(Explanation);
        var feedback=Ui.Text("",12,Ui.Muted);feedback.Margin=new Thickness(0);
        var buttons=new WrapPanel();
        HelpButton=Ui.Button("Página oficial da Microsoft (x64)",()=>
        {
            try{if(open is not null)open(NativeRuntime.HelpUri);else Process.Start(new ProcessStartInfo(NativeRuntime.HelpUri.AbsoluteUri){UseShellExecute=true});}
            catch{feedback.Text="Não foi possível abrir o navegador. Endereço oficial: "+NativeRuntime.HelpUri.AbsoluteUri;}
        });
        CloseButton=Ui.Button("Fechar",Hide);buttons.Children.Add(HelpButton);buttons.Children.Add(CloseButton);stack.Children.Add(buttons);stack.Children.Add(feedback);
        Content=new Border{Background=Brushes.White,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Child=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
    }
    public void Open(OrbWindow orb)
    {
        var work=SystemParameters.WorkArea;Width=Math.Min(440,work.Width-24);Left=Math.Clamp(orb.Left+orb.Width-Width,work.Left+12,work.Right-Width-12);
        Top=orb.Top+orb.Height+8;MaxHeight=Math.Max(120,work.Bottom-Top-12);Show();
    }
}
