using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interpreter;
using ProLang.Parse;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Compiler;

public sealed class Compilation
{
    private BoundGlobalScope? _globalScope;

    public Compilation(SyntaxTree syntaxTree) : this(null,syntaxTree)
    {
        SyntaxTree = syntaxTree;
    }

    private Compilation(Compilation? previous, SyntaxTree syntaxTree)
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

    internal EvaluationResult Evaluate(Dictionary<VariableSymbol, object> variables)
    {
        var diagnostics = SyntaxTree.Diagnostics.Concat(GlobalScope.Diagnostics).ToImmutableArray();

        if (diagnostics.Any())
        { 
            return new EvaluationResult(diagnostics, null!);
        }

        var program = Binder.BindProgram(GlobalScope);

        if (program.Diagnostics.Any())
        {
            return new EvaluationResult(program.Diagnostics.ToImmutableArray(), null!);
        }

        var evaluator = new Evaluator(program, variables);
        
        var value = evaluator.Evaluate();

        return new EvaluationResult(ImmutableArray<Diagnostic>.Empty, value);

    }
    
    public void EmitTree(TextWriter writer)
    {
        var program = Binder.BindProgram(GlobalScope);
        
        program.Statement.WriteTo(writer);
    }

    public Compilation? Previous { get; }
    public SyntaxTree SyntaxTree { get; }
}