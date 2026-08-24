namespace ProLang.Symbols.Modules;

public sealed class IOModule : BuiltInModule
{
    public override string Name => "io";

    public override string Summary => "Printing to the console and reading a line back.";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.Print,
        BuiltInFunctions.ReadInput
    ];
}
