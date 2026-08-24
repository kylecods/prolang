namespace ProLang.Symbols.Modules;

public sealed class MathModule : BuiltInModule
{
    public override string Name => "math";

    public override string Summary => "The integer arithmetic the language does not have as operators.";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.Random,
        BuiltInFunctions.Min,
        BuiltInFunctions.Max
    ];
}
