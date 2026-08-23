using ProLang.Symbols;

namespace ProLang.Intermediate;

/// <summary>
/// A top-level function used as a value, as in <c>on_press: increment</c>.
/// </summary>
/// <remarks>
/// Carries the resolved <see cref="FunctionSymbol"/> rather than a name, so the backends never have
/// to look one up: the .NET emitter reaches straight for the <c>MethodDefinition</c> it already
/// built, and the C emitter writes the function's name.
/// </remarks>
internal sealed class BoundFunctionReference : BoundExpression
{
    public BoundFunctionReference(FunctionSymbol function, FunctionTypeSymbol type)
    {
        Function = function;
        Type = type;
    }

    public override BoundNodeKind Kind => BoundNodeKind.BoundFunctionReference;

    public override TypeSymbol Type { get; }

    public FunctionSymbol Function { get; }
}
