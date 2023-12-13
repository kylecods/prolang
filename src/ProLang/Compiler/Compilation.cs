using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interpreter;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Compiler;

public sealed class Compilation
{
    private BoundGlobalScope _globalScope;

    public Compilation(SyntaxTree syntaxTree) : this(null,syntaxTree)
    {
        SyntaxTree = syntaxTree;
    }

    private Compilation(Compilation previous, SyntaxTree syntaxTree)
    {
        Previous = previous;
        SyntaxTree = syntaxTree;
    }

    internal BoundGlobalScope GlobalScope
    {
        get
        {
            if (_globalScope == null)
            {
                var globalScope = Binder.BindGlobalScope(Previous?.GlobalScope, SyntaxTree.Root);

                Interlocked.CompareExchange(ref _globalScope, globalScope, null);
            }

            return _globalScope;
        }
    }

    public Compilation ContinueWith(SyntaxTree syntaxTree)
    {
        return new Compilation(this, syntaxTree);
    }

    public IEnumerable<EvaluationResult> Evaluate(Dictionary<VariableSymbol, object> variables)
    {
        var diagnostics = SyntaxTree.Diagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();

        if (diagnostics.Any())
        {
            yield return new EvaluationResult(diagnostics, null!);
        }

        foreach (var statement in GlobalScope.Statements )
        {
            var evaluator = new Evaluator(statement, variables);
            var value = evaluator.Evaluate();

            yield return new EvaluationResult(Array.Empty<Diagnostic>(), value);
        }
        
    }

    public Compilation Previous { get; }
    public SyntaxTree SyntaxTree { get; }
}