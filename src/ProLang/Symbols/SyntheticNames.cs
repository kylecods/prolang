namespace ProLang.Symbols;

/// <summary>
/// Names the binder generates for symbols that have no counterpart in the source.
/// </summary>
/// <remarks>
/// <para>
/// The binder creates these and every backend has to recognise them, so they are a contract
/// between phases rather than an implementation detail of either. They were previously repeated
/// as string literals across <c>Binder.cs</c>, <c>Emitter.cs</c>, <c>CEmitter.cs</c>, and
/// <c>MsilDisassembler.cs</c>, where a rename in one place would have gone unnoticed in the rest.
/// </para>
/// <para>
/// The double underscore prefix keeps them out of the space a ProLang program can name: the lexer
/// permits leading underscores in identifiers, but nothing in the language produces this prefix.
/// </para>
/// </remarks>
internal static class SyntheticNames
{
    /// <summary>
    /// The generated entry point.
    /// </summary>
    /// <remarks>
    /// Wraps <see cref="UserMain"/> with the runtime's output initialisation and flush. It exists
    /// even when the program has no global statements, which is why backends test for it by name
    /// rather than assuming <c>BoundProgram.MainFunction</c> is the user's own function.
    /// </remarks>
    public const string Main = "__Main";

    /// <summary>
    /// The user's <c>main</c>, renamed so that <see cref="Main"/> can take its place.
    /// </summary>
    public const string UserMain = "__UserMain";

    /// <summary>
    /// The generated function that runs <c>global</c> variable initializers.
    /// </summary>
    /// <remarks>
    /// Exists only when the program declares at least one <c>global</c>. The .NET backend calls it
    /// from <see cref="Main"/> (or from a static constructor for libraries); the C backends call it
    /// at the top of <see cref="UserMain"/>, which covers both the desktop entry point and the PSP
    /// bootstrap, since both reach user code through it.
    /// </remarks>
    public const string GlobalsInit = "__GlobalsInit";
}
