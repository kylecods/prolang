using System.Text;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang;

internal class Program
{
    private static void Main(string[] args)
    {
        var fileName = args[0];

        var content = File.ReadAllText(fileName);

        var text = SourceText.From(content);
        
        var syntaxTree = SyntaxTree.Parse(text);

        if (!syntaxTree.Diagnostics.Any())
        {
            // syntaxTree.Root.WriteTo(Console.Out);
        }
        else
        {
            foreach (var diagnostic in syntaxTree.Diagnostics)
            {
                var lineIndex = syntaxTree.Text.GetLineIndex(diagnostic.Span.Start);
                var errorLine = syntaxTree.Text.Lines[lineIndex];
                var lineNumber = lineIndex + 1;
                var character = diagnostic.Span.Start - errorLine.Start + 1;
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;

                Console.Write($"({lineNumber}, {character}): ");
                Console.WriteLine(diagnostic);
                Console.ResetColor();

                var prefixSpan = TextSpan.FromBounds(errorLine.Start, diagnostic.Span.Start);
                var suffixSpan = TextSpan.FromBounds(diagnostic.Span.End, errorLine.End);

                var prefix = syntaxTree.Text.ToString(prefixSpan);
                var error = syntaxTree.Text.ToString(diagnostic.Span);
                var suffix = syntaxTree.Text.ToString(suffixSpan);
                
                Console.Write("   ");
                Console.Write(prefix);
                
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(error);
                Console.ResetColor();
                
                Console.Write(suffix);
                
                Console.WriteLine();
            }
            Console.WriteLine();
        }
    }
}