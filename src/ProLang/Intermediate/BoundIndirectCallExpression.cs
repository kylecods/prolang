using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

/// <summary>
/// A call through a function value rather than to a named function: <c>paint(surface, 0)</c> where
/// <c>paint</c> is a parameter of function type.
/// </summary>
internal sealed class BoundIndirectCallExpression : BoundExpression
{
    public BoundIndirectCallExpression(BoundExpression target, FunctionTypeSymbol functionType,
        ImmutableArray<BoundExpression> arguments)
    {
        Target = target;
        FunctionType = functionType;
        Arguments = arguments;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundIndirectCallExpression;

    public override TypeSymbol Type => FunctionType.ReturnType;

    /// <summary>The expression producing the function value — in practice a variable or parameter.</summary>
    public BoundExpression Target { get; }

    public FunctionTypeSymbol FunctionType { get; }

    public ImmutableArray<BoundExpression> Arguments { get; }
}
