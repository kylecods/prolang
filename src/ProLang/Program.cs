using System.Text;
using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang;

internal class Program
{
    private static void Main()
    {

        Compilation? previous = null;

        Dictionary<VariableSymbol, object> variables = new ();

        var textBuilder = new StringBuilder();

        var showTree = false;

        var showProgram = false;

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            
            if (textBuilder.Length == 0)
            {
                Console.Write(">> ");   
            }
            else
            {
                Console.Write(". ");
            }
            
            Console.ResetColor();

            var input = Console.ReadLine();

            var isBlank = string.IsNullOrWhiteSpace(input);

            if (textBuilder.Length == 0)
            {
                if (isBlank)
                {
                    break;
                }
                else if (input == "#ST")
                {
                    showTree = !showTree;
                    Console.WriteLine(showTree ? "Showing parse trees." : "Not showing parse trees");
                    continue;
                }
                else if (input == "#SP")
                {
                    showProgram = !showProgram;
                    Console.WriteLine(showProgram ? "Showing bound tree." : "Not showing bound tree.");
                    continue;
                }
                else if (input == "#cls")
                {
                    Console.Clear();
                    continue;
                }
                else if (input == "#reset")
                {
                    previous = null;
                    variables.Clear();
                    continue;
                }
            }

            textBuilder.Append(input);

            var text = textBuilder.ToString();

            var syntaxTree = SyntaxTree.Parse(text);

            if (!isBlank && syntaxTree.Diagnostics.Any())
            {
                continue;
            }
            
            var compilation = previous == null ? new Compilation(syntaxTree) : previous.ContinueWith(syntaxTree);

            if (showTree)
            {
                syntaxTree.Root.WriteTo(Console.Out);
            }
            if (showProgram)
            {
                compilation.EmitTree(Console.Out);
            }
                
            var result = compilation.Evaluate(variables);
            
            if (!result.Diagnostics.Any())
            {
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(result.Value);
                Console.ResetColor();
                previous = compilation;
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

            textBuilder.Clear();
        }

    }
}