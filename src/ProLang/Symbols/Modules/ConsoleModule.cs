namespace ProLang.Symbols.Modules;

public sealed class ConsoleModule : BuiltInModule
{
    public override string Name => "console";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.ConsoleWrite,
        BuiltInFunctions.ConsoleSetCursor,
        BuiltInFunctions.ConsoleHideCursor,
        BuiltInFunctions.ConsoleSetColor,
        BuiltInFunctions.ConsoleResetColor,
        BuiltInFunctions.ConsoleKeyAvailable,
        BuiltInFunctions.ConsoleReadKey,
        BuiltInFunctions.ThreadSleep,
    ];
}
