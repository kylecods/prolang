namespace ProLang.Symbols;

public sealed class ParameterSymbol : LocalVariableSymbol
{
    public ParameterSymbol(string name, TypeSymbol type, int ordinal, object? defaultValue = null)
        : base(name, true, type)
    {
        Ordinal = ordinal;
        DefaultValue = defaultValue;
    }

    public override SymbolKind Kind => SymbolKind.Parameter;

    public int Ordinal { get; }

    /// <summary>
    /// The constant this parameter takes when the caller omits it, or <see langword="null"/> when
    /// the parameter is required.
    /// </summary>
    /// <remarks>
    /// A constant rather than a bound expression on purpose. The binder materialises a fresh
    /// literal at each call site, so a default never has to be re-bound in the caller's scope and
    /// no backend sees anything it does not already handle — the argument list reaching
    /// <c>BoundCallExpression</c> is always complete and positional.
    /// </remarks>
    public object? DefaultValue { get; }

    public bool IsOptional => DefaultValue != null;
}
