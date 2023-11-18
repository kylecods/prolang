using ProLang.Syntax;
using ProLang.Utils;

namespace ProLang;

class Program
{
    static void Main(string[] args)
    {
        var syntaxTree = SyntaxTree.Parse("(1+2) * (4/2) + (4-3)");

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