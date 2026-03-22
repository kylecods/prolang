namespace ProLang.Symbols.Modules;

public sealed class IOModule : BuiltInModule
{
    public override string Name => "io";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.Print,
        BuiltInFunctions.ReadInput
    ];
}
