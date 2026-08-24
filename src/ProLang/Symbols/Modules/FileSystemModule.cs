namespace ProLang.Symbols.Modules;

public sealed class FileSystemModule : BuiltInModule
{
    public override string Name => "fs";

    public override string Summary => "Reading and writing whole files.";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.FileExists,
        BuiltInFunctions.ReadFile,
        BuiltInFunctions.ReadFileBytes,
        BuiltInFunctions.WriteFile
    ];
}
