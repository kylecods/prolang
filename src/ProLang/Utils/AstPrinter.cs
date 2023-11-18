using ProLang.Syntax;

namespace ProLang.Utils;

internal static class AstPrinter
{
    public static void Print(SyntaxNode node,string indent = "", bool isLast = true)
    {
        var marker = isLast ? "└──" : "├──";
        
        Console.Write(indent);
        Console.Write(marker);
        Console.Write(node.Kind);

        if (node is SyntaxToken { Value: not null } t)
        {
            Console.Write(" ");
            Console.Write(t.Value);
        }
        
        Console.WriteLine();

        indent += isLast ? "    " : "|  ";

        var lastChild = node.GetChildren().LastOrDefault();

        foreach (var child in node.GetChildren())
        {
            Print(child,indent, child == lastChild);
        }
    }
}