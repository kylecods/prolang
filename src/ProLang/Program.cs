using ProLang.Syntax;
using ProLang.Utils;

namespace ProLang;

internal class Program
{
    private static void Main(string[] args)
    {
        var syntaxTree = SyntaxTree.Parse("let a = true");

        if (!syntaxTree.Diagnostics.Any())
        {
            AstPrinter.Print(syntaxTree.Root);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var diagnostic in syntaxTree.Diagnostics)
            {
                Console.WriteLine(diagnostic);
            }
        }
    }
}