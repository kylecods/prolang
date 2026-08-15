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
    private static readonly Dictionary<FunctionSymbol, IntrinsicEmitter> Table = Build();

    /// <summary>
    /// Looks up the emitter for <paramref name="function"/>.
    /// </summary>
    /// <returns>True if <paramref name="function"/> is a builtin with a dedicated IL sequence.</returns>
    public static bool TryGetEmitter(FunctionSymbol function, out IntrinsicEmitter emitter) =>
        Table.TryGetValue(function, out emitter!);

    private static Dictionary<FunctionSymbol, IntrinsicEmitter> Build() => new()
    {
        // ── Console and input ────────────────────────────────────────────────────────────
        // All of these live in ProLang.Runtime.ConsoleOps, which handles output redirection in
        // ordinary C# rather than in six hand-rolled IL branches.
        [BuiltInFunctions.ReadInput] = Call(RuntimeLibrary.ConsoleOps, "ReadInput"),
        [BuiltInFunctions.ConsoleWrite] = Call(RuntimeLibrary.ConsoleOps, "Write", "System.String"),
        [BuiltInFunctions.ConsoleSetCursor] = Call(RuntimeLibrary.ConsoleOps, "SetCursor", "System.Int32", "System.Int32"),
        [BuiltInFunctions.ConsoleSetColor] = Call(RuntimeLibrary.ConsoleOps, "SetColor", "System.Int32"),
        [BuiltInFunctions.ConsoleResetColor] = Call(RuntimeLibrary.ConsoleOps, "ResetColor"),
        [BuiltInFunctions.ConsoleHideCursor] = Call(RuntimeLibrary.ConsoleOps, "HideCursor"),
        [BuiltInFunctions.ConsoleKeyAvailable] = Call(RuntimeLibrary.ConsoleOps, "KeyAvailable"),
        [BuiltInFunctions.ConsoleReadKey] = Call(RuntimeLibrary.ConsoleOps, "ReadKey"),

        // ── Math ─────────────────────────────────────────────────────────────────────────
        // Min and Max map exactly onto System.Math, so they stay direct calls; only Random
        // needed adapting.
        [BuiltInFunctions.Min] = Call("System.Math", "Min", "System.Int32", "System.Int32"),
        [BuiltInFunctions.Max] = Call("System.Math", "Max", "System.Int32", "System.Int32"),
        [BuiltInFunctions.Random] = Call(RuntimeLibrary.MathOps, "Random", "System.Int32"),

        // ── Strings ──────────────────────────────────────────────────────────────────────
        // length and indexOf map exactly onto System.String. charAt and substring do not —
        // ProLang has no char type and its end index is exclusive — so they are adapted.
        [BuiltInFunctions.StringLength] = CallVirt("System.String", "get_Length"),
        [BuiltInFunctions.StringIndexOf] = CallVirt("System.String", "IndexOf", "System.String"),
        [BuiltInFunctions.StringCharAt] = Call(RuntimeLibrary.StringOps, "CharAt", "System.String", "System.Int32"),
        [BuiltInFunctions.StringSubstring] = Call(RuntimeLibrary.StringOps, "Substring", "System.String", "System.Int32", "System.Int32"),

        // ── Arrays ───────────────────────────────────────────────────────────────────────
        [BuiltInFunctions.ArrayLength] = ctx =>
        {
            // The array is already on the stack. ldlen yields a native int, so narrow it.
            ctx.IL.Emit(OpCodes.Ldlen);
            ctx.IL.Emit(OpCodes.Conv_I4);
        },

        // ── File system ──────────────────────────────────────────────────────────────────
        [BuiltInFunctions.FileExists] = Call("System.IO.File", "Exists", "System.String"),
        [BuiltInFunctions.ReadFile] = Call("System.IO.File", "ReadAllText", "System.String"),
        [BuiltInFunctions.ReadFileBytes] = Call("System.IO.File", "ReadAllBytes", "System.String"),
        [BuiltInFunctions.WriteFile] = Call("System.IO.File", "WriteAllText", "System.String", "System.String"),

        // ── Threading ────────────────────────────────────────────────────────────────────
        [BuiltInFunctions.ThreadSleep] = Call("System.Threading.Thread", "Sleep", "System.Int32"),
    };

    /// <summary>Emits a static call to a BCL method, with the arguments already on the stack.</summary>
    private static IntrinsicEmitter Call(string typeName, string methodName, params string[] parameterTypeNames) =>
        ctx => EmitCall(ctx, OpCodes.Call, typeName, methodName, parameterTypeNames);

    /// <summary>Emits an instance call, with the receiver and arguments already on the stack.</summary>
    private static IntrinsicEmitter CallVirt(string typeName, string methodName, params string[] parameterTypeNames) =>
        ctx => EmitCall(ctx, OpCodes.Callvirt, typeName, methodName, parameterTypeNames);

    private static void EmitCall(
        IntrinsicContext ctx,
        OpCode opCode,
        string typeName,
        string methodName,
        string[] parameterTypeNames)
    {
        var method = ctx.Method(typeName, methodName, parameterTypeNames);

        if (method == null)
        {
            // ResolveMethod has already reported the failure. Emitting nothing here would leave
            // the arguments stranded on the evaluation stack and produce an assembly that only
            // fails when the JIT reaches it, so the diagnostic is the whole response — the
            // emitter must not go on to write this assembly.
            return;
        }

        ctx.IL.Emit(opCode, method);
    }
}
