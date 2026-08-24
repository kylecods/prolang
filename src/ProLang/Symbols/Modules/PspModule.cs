namespace ProLang.Symbols.Modules;

public sealed class PspModule : BuiltInModule
{
    public override string Name => "psp";

    public override string Summary => "Graphics and input for the PlayStation Portable. Requires --emit-psp.";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.PspInit,
        BuiltInFunctions.PspClear,
        BuiltInFunctions.PspFillRect,
        BuiltInFunctions.PspDrawText,
        BuiltInFunctions.PspSwapBuffers,
        BuiltInFunctions.PspVsync,
        BuiltInFunctions.PspButtonsHeld,
        BuiltInFunctions.PspButtonPressed,
        BuiltInFunctions.PspDrawLine,
    ];
}
