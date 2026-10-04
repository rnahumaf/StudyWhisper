using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace StudyWhisper.Core;

public static class MarkdownContent
{
    private static readonly MarkdownPipeline Parser=new MarkdownPipelineBuilder().DisableHtml().Build();
    public static MarkdownDocument Parse(string text)=>Markdown.Parse(Pipeline.Compact(text),Parser);
    public static string PlainText(string text)
    {
        var output=new StringBuilder();Blocks(Parse(text),output,0);return output.ToString().TrimEnd();
    }
    private static void Blocks(ContainerBlock blocks,StringBuilder output,int depth)
    {
        if(depth>32)return;
        foreach(var block in blocks)
        {
            if(block is ListBlock list)
            {
                var n=int.TryParse(list.OrderedStart,out var start)?start:1;
                foreach(var child in list.OfType<ListItemBlock>())
                {var item=new StringBuilder();Blocks(child,item,depth+1);output.Append(list.IsOrdered?$"{n++}. ":"• ").AppendLine(item.ToString().TrimEnd());}
                output.AppendLine();
            }
            else if(block is CodeBlock code)output.AppendLine(code.Lines.ToString()).AppendLine();
            else if(block is LeafBlock leaf){if(leaf.Inline is not null)Inlines(leaf.Inline,output,depth+1);output.AppendLine().AppendLine();}
            else if(block is ContainerBlock container)Blocks(container,output,depth+1);
        }
    }
    private static void Inlines(ContainerInline container,StringBuilder output,int depth)
    {
        if(depth>32)return;
        foreach(var inline in container)
            switch(inline)
            {
                case LiteralInline literal:output.Append(literal.Content.ToString());break;
                case CodeInline code:output.Append(code.Content);break;
                case LineBreakInline br:if(br.IsHard)output.AppendLine();else output.Append(' ');break;
                case AutolinkInline link:output.Append(link.Url);break;
                case ContainerInline child:Inlines(child,output,depth+1);break;
            }
    }
}
