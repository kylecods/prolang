namespace ProLang.Symbols.Modules;

public sealed class ArrayModule : BuiltInModule
{
    public override string Name => "array";

    public override string Summary => "Creating a fixed-length array and asking how long one is.";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.ArrayLength,
        BuiltInFunctions.ArrayNew,
    ];
}
