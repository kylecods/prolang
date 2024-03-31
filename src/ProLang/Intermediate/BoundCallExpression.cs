using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundCallExpression : BoundExpression
{
    public BoundCallExpression(FunctionSymbol function, ImmutableArray<BoundExpression> arguments)
    {
        Function = function;

        Arguments = arguments;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundCallExpression;
    public override TypeSymbol Type => Function.Type;
    
    public FunctionSymbol Function { get; }
    
    public ImmutableArray<BoundExpression> Arguments { get; }
}