using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interpreter;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Compiler;

public sealed class ProLangCompilation : Compilation
{
    public ProLangCompilation(params SyntaxTree[] syntaxTrees) : base(null,syntaxTrees)
    {
    }
    
    private ProLangCompilation(ProLangCompilation? previous, params SyntaxTree[] syntaxTrees) : base(previous,syntaxTrees)
    {
    }

    public ProLangCompilation ContinueWith(SyntaxTree syntaxTree)
    {
        return new ProLangCompilation(this, syntaxTree);
    }

    internal EvaluationResult Evaluate(Dictionary<VariableSymbol, object> variables)
    {
        var parseDiagnostics = SyntaxTrees.SelectMany(st => st.Diagnostics);

        var diagnostics = parseDiagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();
        
        if (diagnostics.Any())
        { 
            return new EvaluationResult(diagnostics, null!);
        }

        var program = Binder.BindProgram(GlobalScope);

        var appPath = Environment.GetCommandLineArgs()[0];
        var appDirectory = Path.GetDirectoryName(appPath);
        var cfgPath = Path.Combine(appDirectory!, "cfg.dot");
        var cfgStatement = !program.Statement.Statements.Any() && program.Functions.Any() ?
                program.Functions.Last().Value : program.Statement;

        var cfg = ControlFlowGraph.Create(cfgStatement);
        using (var streamWriter = new StreamWriter(cfgPath))
        {
            cfg.WriteTo(streamWriter);
        }

        if (program.Diagnostics.Any())
        {
            return new EvaluationResult(program.Diagnostics.ToImmutableArray(), null!);
        }

        var evaluator = new Evaluator(program, variables);
        
        var value = evaluator.Evaluate();

        return new EvaluationResult(ImmutableArray<Diagnostic>.Empty, value);

    }
    
    public override void EmitTree(TextWriter writer)
    {
        var program = Binder.BindProgram(GlobalScope);

        if (program.Statement.Statements.Any())
        {
            program.Statement.WriteTo(writer);
        }
        else
        {
            foreach (var functionBody in program.Functions)
            {
                if (!GlobalScope.Functions.Contains(functionBody.Key))
                {
                    continue;
                }

                functionBody.Key.WriteTo(writer);
                writer.WriteLine();
                functionBody.Value.WriteTo(writer);
            }
        }
    }
    public void EmitTree(FunctionSymbol symbol, TextWriter writer)
    {
        var program = Binder.BindProgram(GlobalScope);
        if (!program.Functions.TryGetValue(symbol, out var body))
        {
            return;
        }

        symbol.WriteTo(writer);
        writer.WriteLine();
        body.WriteTo(writer);
    }

    public ImmutableArray<FunctionSymbol> Functions => GlobalScope.Functions;

    public ImmutableArray<VariableSymbol> Variables => GlobalScope.Variables;

    public IEnumerable<Symbol> GetSymbols()
    {
        var submission = this;

        var seenSymbols = new HashSet<string>();

        while (submission != null)
        {

            foreach (var function in submission.Functions)
            {
                if (seenSymbols.Add(function.Name))
                {
                    yield return function;
                }
            }

            foreach (var variable in submission.Variables)
            {
                if (seenSymbols.Add(variable.Name))
                {
                    yield return variable;
                }
            }

            submission = submission.Previous as ProLangCompilation;

        }
    }

}