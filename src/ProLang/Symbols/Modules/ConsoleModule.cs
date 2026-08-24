namespace ProLang.Symbols.Modules;

public sealed class ConsoleModule : BuiltInModule
{
    public override string Name => "console";

    public override string Summary => "Drawing to the terminal, reading keys without waiting, and the clock.";

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
        BuiltInFunctions.TimeMillis,
    ];
}
