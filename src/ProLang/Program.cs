using ProLang.Syntax;
using ProLang.Utils;

namespace ProLang;

class Program
{
    static void Main(string[] args)
    {
        var syntaxTree = SyntaxTree.Parse("let a = 1+2");

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