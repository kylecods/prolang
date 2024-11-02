using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Cli;

internal sealed class ProLangRepl : Repl
{
    private bool _loadingSubmission;

    private static readonly ProLangCompilation emptyCompilation = new();
    private ProLangCompilation? _previous;
    private bool _showTree;
    private bool _showProgram;
    private readonly Dictionary<VariableSymbol, object> _variables = new();

    public ProLangRepl()
    {
        LoadSubmissions();
    }

    protected override void RenderLine(string line)
    {
        var tokens = SyntaxTree.ParseTokens(line);
        foreach (var token in tokens)
        {
            var isKeyword = token.Kind.ToString().EndsWith("Keyword");
            var isNumber = token.Kind == SyntaxKind.NumberToken;
            var isIdentifier = token.Kind == SyntaxKind.IdentifierToken;
            var isString = token.Kind == SyntaxKind.StringToken;

            if (isKeyword)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
            } else if (isIdentifier)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
            }
            else if (isNumber)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
            } else if (isString)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
            }

            Console.Write(token.Text);

            Console.ResetColor();
        }
    }

    [MetaCommand("cls", "Clears the screen")]
    private void EvaluateCls()
    {
        Console.Clear();
    }

    [MetaCommand("reset", "Clears all previous submissions")]
    private void EvaluateReset()
    {
        _previous = null;

        _variables.Clear();
        ClearSubmissions();
    }

    [MetaCommand("showTree", "Shows the parse tree")]
    private void EvaluateShowTree()
    {
        _showTree = !_showTree;

        Console.WriteLine(_showTree ? "Showing parse trees." : "Not showing parse trees.");
    }

    [MetaCommand("showProgram", "Shows the bound tree")]
    private void EvaluateShowProgram()
    {
        _showProgram = !_showProgram;

        Console.WriteLine(_showProgram ? "Showing bound tree" : "Not showing bound tree.");
    }

    [MetaCommand("load", "Loads a script file")]
    private void EvaluateLoad(string path)
    {
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"error: file does not exist '{path}'");
            Console.ResetColor();

            return;
        }

        var text = File.ReadAllText(path);

        EvaluateSubmission(text);
    }

    [MetaCommand("ls", "Lists all symbols")]
    private void EvaluateLs()
    {
        if (_previous == null)
        {
            return;
        }
        var compilation = _previous ?? emptyCompilation;

        var symbols = compilation.GetSymbols().OrderBy(s => s.Kind).ThenBy(s=> s.Name);

        foreach (var symbol in symbols)
        {
            symbol.WriteTo(Console.Out);
            Console.WriteLine();
        }
    }

    [MetaCommand("dump", "Shows bound tree of a given function")]
    private void EvaluateDump(string functionName)
    {
        if (_previous == null)
        {
            return;
        }

        var compilation = _previous ?? emptyCompilation;

        var symbol = compilation.GetSymbols().OfType<FunctionSymbol>().SingleOrDefault(f => f.Name == functionName);

        if (symbol == null) 
        {
            Console.ForegroundColor= ConsoleColor.Red;
            Console.WriteLine($"error: function '{functionName}' does not exist");
            Console.ResetColor();
            return;
        }

        compilation.EmitTree(symbol,Console.Out);
    }

   
    protected override bool IsCompleteSubmission(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var lastTwoLinesAreBlank = text.Split(Environment.NewLine)
            .Reverse().TakeWhile(s => string.IsNullOrEmpty(s))
            .Take(2).Count() == 2;

        if (lastTwoLinesAreBlank)
        {
            return true;
        }

        var syntaxTree = SyntaxTree.Parse(text);

        if (syntaxTree.Root.Declarations.Last().GetLastToken().IsMissing)
        {
            return false;
        }

        return true;
    }

    protected override void EvaluateSubmission(string text)
    {
        var syntaxTree = SyntaxTree.Parse(text);

        var compilation = _previous == null! ? new ProLangCompilation(syntaxTree) : _previous.ContinueWith(syntaxTree);

        if (_showTree)
        {
            syntaxTree.Root.WriteTo(Console.Out);
        }

        if (_showProgram)
        {
            compilation.EmitTree(Console.Out);
        }

        var result = compilation.Evaluate(_variables);

        if (!result.Diagnostics.Any())
        {
            if (result.Value != null)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(result.Value);
                Console.ResetColor();
            }
            _previous = compilation;
        }
        else
        {
            
            Console.Out.WriteDiagnostics(result.Diagnostics);
        }
    }

    private static string GetSubmissionsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var submissionsDirectory = Path.Combine(localAppData, "ProLang", "Sumbmissions");

        return submissionsDirectory;
    }
    private void LoadSubmissions()
    {
        var submissionsDirectory = GetSubmissionsDirectory();

        if (!Directory.Exists(submissionsDirectory))
        {
            return;
        }

        var files = Directory.GetFiles(submissionsDirectory).OrderBy(f => f).ToArray();

        if (files.Length == 0) 
        {
            return;
        }

        Console.ForegroundColor= ConsoleColor.DarkGray;

        Console.WriteLine($"Loaded {files.Length} submission(s)");
        Console.ResetColor();

        _loadingSubmission = true;

        foreach (var file in files) 
        { 
            var text = File.ReadAllText(file);

            EvaluateSubmission(text);
        }

        _loadingSubmission = false;
    }

    private static void ClearSubmissions()
    {
        Directory.Delete(GetSubmissionsDirectory(), true);
    }

    private void SaveSubmission(string text)
    {
        if (_loadingSubmission) 
        {
            return;
        }

        var submissionsDirectory = GetSubmissionsDirectory();

        Directory.CreateDirectory(submissionsDirectory);

        var count = Directory.GetFiles(submissionsDirectory).Length;
        var name = $"submission{count:0000}";
        var fileName = Path.Combine(submissionsDirectory, name);
        File.WriteAllText(fileName, text);
    }
}