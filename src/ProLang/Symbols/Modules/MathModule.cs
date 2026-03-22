namespace ProLang.Symbols.Modules;

public sealed class MathModule : BuiltInModule
{
    public override string Name => "math";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.Random,
        BuiltInFunctions.Min,
        BuiltInFunctions.Max
    ];
}
