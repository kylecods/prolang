using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Syntax;

namespace ProLang.Compiler;

public abstract class Compilation
{
    private BoundGlobalScope? _globalScope;

    public Compilation(params SyntaxTree[] syntaxTrees) : this(null,syntaxTrees)
    {
    }

    protected Compilation(Compilation? previous, params SyntaxTree[] syntaxTrees)
    {
        Previous = previous;
        SyntaxTrees = syntaxTrees.ToImmutableArray();
    }
    

    internal BoundGlobalScope GlobalScope
    {
        get
        {
            if (_globalScope == null)
            {
                var globalScope = Binder.BindGlobalScope(Previous?.GlobalScope, SyntaxTrees);

                Interlocked.CompareExchange(ref _globalScope, globalScope, null);
            }

            return _globalScope;
        }
    }
    public abstract void EmitTree(TextWriter writer);

    public Compilation? Previous { get; }
    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }

}