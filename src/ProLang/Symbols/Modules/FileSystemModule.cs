namespace ProLang.Symbols.Modules;

public sealed class FileSystemModule : BuiltInModule
{
    public override string Name => "fs";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.FileExists,
        BuiltInFunctions.ReadFile,
        BuiltInFunctions.WriteFile
    ];
}
