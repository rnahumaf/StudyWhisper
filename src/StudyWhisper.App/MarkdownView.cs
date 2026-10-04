using System.Diagnostics;
using System.Windows.Documents;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace StudyWhisper.App;

/// <summary>Native text only: no HTML host, downloaded image, script, automatic navigation or embedded resource.</summary>
public static class MarkdownView
{
    public static FrameworkElement Render(string text,Action<Uri>? open=null)
    {
        var root=new StackPanel();Blocks(MarkdownContent.Parse(text),root,open??Open,0);return root;
    }
    public static void Open(Uri uri)
    {
        if(!SafeLinks.TryParse(uri.AbsoluteUri,out var safe))return;
        try{Process.Start(new ProcessStartInfo(safe.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}
    }
    public static Hyperlink Link(string label,string? target,Action<Uri> open)
    {
        var link=new Hyperlink(new Run(label)){Foreground=Ui.Accent,Focusable=false};
        if(SafeLinks.TryParse(target,out var uri))
        {link.ToolTip=uri.AbsoluteUri;link.Cursor=System.Windows.Input.Cursors.Hand;link.Click+=(_,_)=>open(uri);}
        else {link.IsEnabled=false;link.TextDecorations=null;}
        return link;
    }
    private static void Blocks(ContainerBlock blocks,Panel parent,Action<Uri> open,int depth)
    {
        if(depth>32)return;
        foreach(var block in blocks)
        {
            if(block is ListBlock list)
            {
                var n=int.TryParse(list.OrderedStart,out var start)?start:1;
                foreach(var item in list.OfType<ListItemBlock>())
                {
                    var row=new DockPanel{Margin=new Thickness(0,0,0,2)};
                    var marker=Ui.Text(list.IsOrdered?$"{n++}.":"•",14);marker.Width=list.IsOrdered?25:17;marker.Margin=new Thickness(0);DockPanel.SetDock(marker,Dock.Left);row.Children.Add(marker);
                    var content=new StackPanel();Blocks(item,content,open,depth+1);row.Children.Add(content);parent.Children.Add(row);
                }
            }
            else if(block is CodeBlock code)
            {
                var value=Ui.Text(code.Lines.ToString(),12);value.FontFamily=new FontFamily("Consolas");value.LineHeight=18;value.Margin=new Thickness(0);
                parent.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(242,245,247)),CornerRadius=new CornerRadius(4),Padding=new Thickness(7),Margin=new Thickness(0,2,0,7),Child=value});
            }
            else if(block is LeafBlock leaf)
            {
                var value=Ui.Text("",14);value.LineHeight=20;value.Margin=new Thickness(0,0,0,6);
                if(block is HeadingBlock){value.FontWeight=FontWeights.SemiBold;value.FontSize=15;}
                if(leaf.Inline is not null)Inlines(leaf.Inline,value.Inlines,open,depth+1);parent.Children.Add(value);
            }
            else if(block is ContainerBlock container)
            {var nested=new StackPanel{Margin=new Thickness(10,0,0,0)};Blocks(container,nested,open,depth+1);parent.Children.Add(nested);}
        }
    }
    private static void Inlines(ContainerInline container,InlineCollection target,Action<Uri> open,int depth)
    {
        if(depth>32)return;
        foreach(var inline in container)
        {
            switch(inline)
            {
                case LiteralInline literal:target.Add(new Run(literal.Content.ToString()));break;
                case CodeInline code:target.Add(new Run(code.Content){FontFamily=new FontFamily("Consolas"),Background=new SolidColorBrush(Color.FromRgb(242,245,247)),FontSize=12});break;
                case LineBreakInline br:if(br.IsHard)target.Add(new LineBreak());else target.Add(new Run(" "));break;
                case EmphasisInline emphasis:
                    Span span=emphasis.DelimiterCount>=2?new Bold():new Italic();Inlines(emphasis,span.Inlines,open,depth+1);target.Add(span);break;
                case LinkInline link:
                    if(!link.IsImage&&SafeLinks.TryParse(link.Url,out var uri))
                    {var anchor=new Hyperlink{Foreground=Ui.Accent,Focusable=false,ToolTip=uri.AbsoluteUri};Inlines(link,anchor.Inlines,open,depth+1);anchor.Click+=(_,_)=>open(uri);target.Add(anchor);}
                    else Inlines(link,target,open,depth+1);break;
                case AutolinkInline auto:target.Add(Link(auto.Url,auto.Url,open));break;
                case ContainerInline child:Inlines(child,target,open,depth+1);break;
            }
        }
    }
}
