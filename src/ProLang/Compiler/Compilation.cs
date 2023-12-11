using ProLang.Intermediate;
using ProLang.Syntax;

namespace ProLang.Compiler;

internal sealed class Compilation
{
    private BoundGlobalScope _globalScope;

    public Compilation(SyntaxTree tree) : this(null!,tree)
    {
        Tree = tree;
    }

    private Compilation(Compilation previous, SyntaxTree tree)
    {
        Previous = previous;
        Tree = tree;
    }

    internal BoundGlobalScope GlobalScope
    {
        get
        {
            if (_globalScope == null)
            {
                var globalScope = Binder.BindGlobalScope(Previous.GlobalScope, SyntaxTree.Root);

                Interlocked.CompareExchange(ref _globalScope, globalScope, null);
            }

            return _globalScope;
        }
    }

    public Compilation ContinueWith(SyntaxTree syntaxTree)
    {
        return new Compilation(this, syntaxTree);
    }

    public Compilation Previous { get; }
    public SyntaxTree Tree { get; }
}