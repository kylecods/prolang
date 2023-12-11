using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundGlobalScope
{
    public BoundGlobalScope(BoundGlobalScope previous, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<VariableSymbol> variables, ImmutableArray<BoundStatement> statements)
    {
        Previous = previous;
        Diagnostics = diagnostics;
        Variables = variables;
        Statements = statements;
    }

    public BoundGlobalScope Previous { get; }
    
    public ImmutableArray<Diagnostic> Diagnostics { get;  }
    
    public ImmutableArray<VariableSymbol> Variables { get; }
    
    public ImmutableArray<BoundStatement> Statements { get; }
}