using Mono.Cecil;
using Mono.Cecil.Cil;
using ProLang.Symbols;

namespace ProLang.CodeGen.DotNet.Intrinsics;

/// <summary>
/// Maps each builtin function onto the IL that implements it.
/// </summary>
/// <remarks>
/// <para>
/// This replaces a 305-line <c>if / else if</c> chain of reference-equality tests against
/// <see cref="BuiltInFunctions"/> statics, in which adding a builtin meant editing the middle of
/// the emitter's largest method. Dispatch is now a dictionary lookup and each builtin is an
/// independent entry that can be read, changed, and tested on its own.
/// </para>
/// <para>
/// Keying on the <see cref="FunctionSymbol"/> instance is deliberate and matches what the chain
/// did: builtins are singletons on <see cref="BuiltInFunctions"/>, and several share a name
/// (<c>length</c> exists for both arrays and strings), so name-based lookup would be ambiguous.
/// </para>
/// <para>
/// <b>Console builtins and redirected output.</b> Six builtins guard their work with
/// <c>Console.IsOutputRedirected</c> so that a program doing cursor positioning or colour changes
/// still behaves when its output is piped to a file. That guard used to be copy-pasted six times;
/// it now lives in ProLang.Runtime.ConsoleOps as plain C#.
/// </para>
/// </remarks>
internal static class IntrinsicRegistry
{
    private static readonly Dictionary<FunctionSymbol, Intrinsic> Table = Build();

    /// <summary>
    /// Looks up the emitter for <paramref name="function"/>.
    /// </summary>
    /// <returns>True if <paramref name="function"/> is a builtin with a dedicated IL sequence.</returns>
    public static bool TryGet(FunctionSymbol function, out Intrinsic intrinsic) =>
        Table.TryGetValue(function, out intrinsic!);

    private static Dictionary<FunctionSymbol, Intrinsic> Build() => new()
    {
        // ── Console and input ────────────────────────────────────────────────────────────
        // All of these live in ProLang.Runtime.ConsoleOps, which handles output redirection in
        // ordinary C# rather than in six hand-rolled IL branches.
        [BuiltInFunctions.ReadInput] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "ReadInput"),
        [BuiltInFunctions.ConsoleWrite] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "Write", "System.String"),
        [BuiltInFunctions.ConsoleSetCursor] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "SetCursor", "System.Int32", "System.Int32"),
        [BuiltInFunctions.ConsoleSetColor] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "SetColor", "System.Int32"),
        [BuiltInFunctions.ConsoleResetColor] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "ResetColor"),
        [BuiltInFunctions.ConsoleHideCursor] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "HideCursor"),
        [BuiltInFunctions.ConsoleKeyAvailable] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "KeyAvailable"),
        [BuiltInFunctions.ConsoleReadKey] = Intrinsic.Call(RuntimeLibrary.ConsoleOps, "ReadKey"),

        // ── Testing ──────────────────────────────────────────────────────────────────────
        // Flushes the print buffer before throwing, so a failing assert keeps the output that
        // led up to it. See ProLang.Runtime.TestOps.
        [BuiltInFunctions.Assert] = Intrinsic.Call(RuntimeLibrary.TestOps, "Assert", "System.Boolean", "System.String"),

        // ── Math ─────────────────────────────────────────────────────────────────────────
        // Min and Max map exactly onto System.Math, so they stay direct calls; only Random
        // needed adapting.
        [BuiltInFunctions.Min] = Intrinsic.Call("System.Math", "Min", "System.Int32", "System.Int32"),
        [BuiltInFunctions.Max] = Intrinsic.Call("System.Math", "Max", "System.Int32", "System.Int32"),
        [BuiltInFunctions.Random] = Intrinsic.Call(RuntimeLibrary.MathOps, "Random", "System.Int32"),

        // ── Strings ──────────────────────────────────────────────────────────────────────
        // length and indexOf map exactly onto System.String. charAt and substring do not —
        // ProLang has no char type and its end index is exclusive — so they are adapted.
        [BuiltInFunctions.StringLength] = Intrinsic.CallVirt("System.String", "get_Length"),
        [BuiltInFunctions.StringIndexOf] = Intrinsic.CallVirt("System.String", "IndexOf", "System.String"),
        [BuiltInFunctions.StringCharAt] = Intrinsic.Call(RuntimeLibrary.StringOps, "CharAt", "System.String", "System.Int32"),
        [BuiltInFunctions.StringCharCode] = Intrinsic.Call(RuntimeLibrary.StringOps, "CharCode", "System.String", "System.Int32"),
        [BuiltInFunctions.StringSubstring] = Intrinsic.Call(RuntimeLibrary.StringOps, "Substring", "System.String", "System.Int32", "System.Int32"),

        // ── Arrays ───────────────────────────────────────────────────────────────────────
        [BuiltInFunctions.ArrayLength] = Intrinsic.Inline(ctx =>
        {
            // The array is already on the stack. ldlen yields a native int, so narrow it.
            ctx.IL.Emit(OpCodes.Ldlen);
            ctx.IL.Emit(OpCodes.Conv_I4);
        }),

        // ── File system ──────────────────────────────────────────────────────────────────
        [BuiltInFunctions.FileExists] = Intrinsic.Call("System.IO.File", "Exists", "System.String"),
        [BuiltInFunctions.ReadFile] = Intrinsic.Call("System.IO.File", "ReadAllText", "System.String"),
        [BuiltInFunctions.ReadFileBytes] = Intrinsic.Call("System.IO.File", "ReadAllBytes", "System.String"),
        [BuiltInFunctions.WriteFile] = Intrinsic.Call("System.IO.File", "WriteAllText", "System.String", "System.String"),

        // ── Threading ────────────────────────────────────────────────────────────────────
        [BuiltInFunctions.ThreadSleep] = Intrinsic.Call("System.Threading.Thread", "Sleep", "System.Int32"),
        [BuiltInFunctions.TimeMillis] = Intrinsic.Call(RuntimeLibrary.TimeOps, "Millis"),
    };
}
