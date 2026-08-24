using System.Collections.Concurrent;
using System.Reflection;
using ProLang.Text;

namespace ProLang.Syntax;

public abstract class SyntaxNode
{
    /// <summary>
    /// The reflected child properties of each node type, resolved once per type.
    /// </summary>
    /// <remarks>
    /// <see cref="Type.GetProperties()"/> was being called on every single <see cref="GetChildren"/>
    /// call, and <see cref="Span"/> calls <see cref="GetChildren"/> twice and recurses — so reading
    /// one node's span reflected over its entire subtree. A one-shot compile could absorb that; a
    /// language server locating the node under the cursor on every keystroke cannot.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ChildProperties = new();

    private TextSpan? _span;

    public SyntaxNode(SyntaxTree syntaxTree)
    {
        SyntaxTree = syntaxTree;
    }

    public SyntaxTree SyntaxTree { get; }
    public abstract SyntaxKind Kind { get; }

    public TextLocation Location => new TextLocation(SyntaxTree.Text, Span);

    public IEnumerable<SyntaxNode> GetChildren()
    {
        var properties = ChildProperties.GetOrAdd(
            GetType(),
            static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

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
            else if(typeof(SeparatedSyntaxList).IsAssignableFrom(property.PropertyType))
            {
                var separatedSyntaxList = (SeparatedSyntaxList)property.GetValue(this)!;

                foreach (var child in separatedSyntaxList.GetWithSeparators())
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>
    /// The extent of this node in the source, from its first token to its last.
    /// </summary>
    /// <remarks>
    /// Memoised. The syntax tree is built once and never mutated, so a node's span cannot change,
    /// and computing it walks the whole subtree.
    /// </remarks>
    public virtual TextSpan Span => _span ??= ComputeSpan();

    private TextSpan ComputeSpan()
    {
        TextSpan? first = null;
        TextSpan last = default;

        foreach (var child in GetChildren())
        {
            first ??= child.Span;
            last = child.Span;
        }

        // A node with no children at all is degenerate — only malformed input produces one — but
        // this used to be GetChildren().First(), which threw rather than saying so.
        return first is null ? default : TextSpan.FromBounds(first.Value.Start, last.End);
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

    public SyntaxToken GetLastToken()
    {
        if (this is SyntaxToken token)
        {
            return token;
        }

        return GetChildren().Last().GetLastToken();
    }

    /// <summary>The first token this node spans.</summary>
    /// <remarks>
    /// The counterpart of <see cref="GetLastToken"/>. Locating the node under a cursor, and
    /// finding where a declaration starts so the comment above it can be read, both need the
    /// leading edge rather than the trailing one.
    /// </remarks>
    public SyntaxToken GetFirstToken()
    {
        if (this is SyntaxToken token)
        {
            return token;
        }

        return GetChildren().First().GetFirstToken();
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