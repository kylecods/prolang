using ProLang.Compiler;
using ProLang.Symbols;
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

        Compilation? previous = null;

        Dictionary<VariableSymbol, object> variables = new ();

        if (!syntaxTree.Diagnostics.Any())
        {
            
                var compilation = previous == null ? new Compilation(syntaxTree) : previous.ContinueWith(syntaxTree);

                var results = compilation.Evaluate(variables);

                foreach (var result in results)
                {
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
                }
            
            
        }
    }
}