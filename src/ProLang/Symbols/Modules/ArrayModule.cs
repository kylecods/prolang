namespace ProLang.Symbols.Modules;

public sealed class ArrayModule : BuiltInModule
{
    public override string Name => "array";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.ArrayLength,
        BuiltInFunctions.ArrayNew,
    ];
}
