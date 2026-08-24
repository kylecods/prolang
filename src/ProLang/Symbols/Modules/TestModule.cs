namespace ProLang.Symbols.Modules;

/// <summary>
/// <c>import "test"</c> — assertions for programs that test other ProLang code.
/// </summary>
/// <remarks>
/// Kept out of <see cref="IOModule"/> so that <c>assert</c> only enters scope in a program that
/// asks for it. The name is common enough that binding it into every program would shadow a
/// user's own <c>assert</c> function.
/// </remarks>
public sealed class TestModule : BuiltInModule
{
    public override string Name => "test";

    public override string Summary => "Assertions, so a test program written in ProLang can actually fail.";

    public override IReadOnlyList<FunctionSymbol> Functions { get; } =
    [
        BuiltInFunctions.Assert,
    ];
}
