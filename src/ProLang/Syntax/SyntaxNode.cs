using System.Reflection;
using ProLang.Text;

namespace ProLang.Syntax;

public abstract class SyntaxNode
{
    public abstract SyntaxKind Kind { get; }

    public IEnumerable<SyntaxNode> GetChildren()
    {
        var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (typeof(SyntaxNode).IsAssignableFrom(property.PropertyType))
            {
                var child = (SyntaxNode)property.GetValue(this)!;
                
                if (child != null)
                {
                    yield return child;
                }
            }
            else if(typeof(IEnumerable<SyntaxNode>).IsAssignableFrom(property.PropertyType))
            {
                var children = (IEnumerable<SyntaxNode>)property.GetValue(this)!;

                foreach (var child in children)
                {
                    if (child != null)
                    {
                        yield return child;
                    }
                    
                }
            }
        }
    }

    public virtual TextSpan Span
    {
        get
        {
            var first = GetChildren().First().Span;
            var last = GetChildren().Last().Span;

            return TextSpan.FromBounds(first.Start, last.End);
        }
    }
    
    private static void PrettyPrint(TextWriter writer,SyntaxNode node,string indent = "", bool isLast = true)
    {
        var marker = isLast ? "└──" : "├──";
        
        writer.Write(indent);
        writer.Write(marker);
        writer.Write(node.Kind);

        if (node is SyntaxToken { Value: not null } t)
        {
            writer.Write(" ");
            writer.Write(t.Value);
        }
        
        writer.WriteLine();

        indent += isLast ? "   " : "|  ";

        var lastChild = node.GetChildren().LastOrDefault();

        foreach (var child in node.GetChildren())
        {
            PrettyPrint(writer,child,indent, child == lastChild);
        }
    }

    public void WriteTo(TextWriter writer)
    {
        PrettyPrint(writer,this);
    }

    public override string ToString()
    {
        using (var writer = new StringWriter())
        {
            WriteTo(writer);
            return writer.ToString();
        }
    }
}