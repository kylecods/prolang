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
/// it is now <see cref="EmitGuardedByOutputRedirection"/>.
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
        [BuiltInFunctions.ReadInput] = Call("System.Console", "ReadLine"),

        [BuiltInFunctions.ConsoleWrite] = ctx => EmitGuardedByOutputRedirection(
            ctx, ctx.Method("System.Console", "Write", "System.String"), argumentsToDiscard: 1),

        [BuiltInFunctions.ConsoleSetCursor] = ctx => EmitGuardedByOutputRedirection(
            ctx, ctx.Method("System.Console", "SetCursorPosition", "System.Int32", "System.Int32"), argumentsToDiscard: 2),

        [BuiltInFunctions.ConsoleSetColor] = ctx => EmitGuardedByOutputRedirection(
            ctx, ctx.Method("System.Console", "set_ForegroundColor", "System.ConsoleColor"), argumentsToDiscard: 1),

        [BuiltInFunctions.ConsoleResetColor] = ctx => EmitGuardedByOutputRedirection(
            ctx, ctx.Method("System.Console", "ResetColor"), argumentsToDiscard: 0),

        [BuiltInFunctions.ConsoleHideCursor] = EmitHideCursor,
        [BuiltInFunctions.ConsoleKeyAvailable] = EmitKeyAvailable,
        [BuiltInFunctions.ConsoleReadKey] = EmitReadKey,

        // ── Math ─────────────────────────────────────────────────────────────────────────
        [BuiltInFunctions.Min] = Call("System.Math", "Min", "System.Int32", "System.Int32"),
        [BuiltInFunctions.Max] = Call("System.Math", "Max", "System.Int32", "System.Int32"),
        [BuiltInFunctions.Random] = EmitRandom,

        // ── Strings ──────────────────────────────────────────────────────────────────────
        [BuiltInFunctions.StringLength] = CallVirt("System.String", "get_Length"),
        [BuiltInFunctions.StringIndexOf] = CallVirt("System.String", "IndexOf", "System.String"),
        [BuiltInFunctions.StringCharAt] = EmitCharAt,
        [BuiltInFunctions.StringSubstring] = EmitSubstring,

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

    /// <summary>
    /// Emits <c>if (!Console.IsOutputRedirected) target(...)</c>, discarding the already-pushed
    /// arguments when output is redirected.
    /// </summary>
    /// <param name="argumentsToDiscard">
    /// How many values the call would have consumed. They are on the stack already, so the
    /// redirected branch has to pop them to keep the stack balanced across the merge point.
    /// </param>
    private static void EmitGuardedByOutputRedirection(
        IntrinsicContext ctx,
        MethodReference? target,
        int argumentsToDiscard)
    {
        if (target == null)
        {
            return;
        }

        var isRedirected = ctx.Method("System.Console", "get_IsOutputRedirected");

        if (isRedirected == null)
        {
            // Without the guard, fall back to calling unconditionally. Behaviour under
            // redirection is worse than it should be, but the IL stays valid.
            ctx.IL.Emit(OpCodes.Call, target);
            return;
        }

        if (argumentsToDiscard == 0)
        {
            // Nothing on the stack to clean up, so the redirected path is simply a jump over
            // the call. Branching on the true case keeps this to a single branch.
            var skip = ctx.IL.Create(OpCodes.Nop);

            ctx.IL.Emit(OpCodes.Call, isRedirected);
            ctx.IL.Emit(OpCodes.Brtrue, skip);
            ctx.IL.Emit(OpCodes.Call, target);
            ctx.IL.Append(skip);

            return;
        }

        // The arguments are already pushed, so the redirected path has to pop them for the two
        // paths to agree on stack depth where they merge.
        var callTarget = ctx.IL.Create(OpCodes.Nop);
        var done = ctx.IL.Create(OpCodes.Nop);

        ctx.IL.Emit(OpCodes.Call, isRedirected);
        ctx.IL.Emit(OpCodes.Brfalse, callTarget);

        for (var i = 0; i < argumentsToDiscard; i++)
        {
            ctx.IL.Emit(OpCodes.Pop);
        }

        ctx.IL.Emit(OpCodes.Br, done);
        ctx.IL.Append(callTarget);
        ctx.IL.Emit(OpCodes.Call, target);
        ctx.IL.Append(done);
    }

    /// <summary>
    /// Emits <c>if (!Console.IsOutputRedirected) Console.CursorVisible = false</c>.
    /// </summary>
    /// <remarks>
    /// Takes no arguments, so unlike <see cref="EmitGuardedByOutputRedirection"/> there is nothing
    /// to pop and the value to assign is pushed inside the guarded branch.
    /// </remarks>
    private static void EmitHideCursor(IntrinsicContext ctx)
    {
        var isRedirected = ctx.Method("System.Console", "get_IsOutputRedirected");
        var setCursorVisible = ctx.Method("System.Console", "set_CursorVisible", "System.Boolean");

        if (isRedirected == null || setCursorVisible == null)
        {
            return;
        }

        var skip = ctx.IL.Create(OpCodes.Nop);

        ctx.IL.Emit(OpCodes.Call, isRedirected);
        ctx.IL.Emit(OpCodes.Brtrue, skip);
        ctx.IL.Emit(OpCodes.Ldc_I4_0);
        ctx.IL.Emit(OpCodes.Call, setCursorVisible);
        ctx.IL.Append(skip);
    }

    /// <summary>
    /// Emits <c>Console.IsInputRedirected ? false : Console.KeyAvailable</c>.
    /// </summary>
    /// <remarks>
    /// Guards on input rather than output redirection, and yields a value on both paths, so it
    /// cannot share <see cref="EmitGuardedByOutputRedirection"/>. Returning false when stdin is
    /// redirected stops an input-polling loop from throwing in a non-interactive context.
    /// </remarks>
    private static void EmitKeyAvailable(IntrinsicContext ctx)
    {
        var isInputRedirected = ctx.Method("System.Console", "get_IsInputRedirected");
        var keyAvailable = ctx.Method("System.Console", "get_KeyAvailable");

        if (keyAvailable == null)
        {
            return;
        }

        if (isInputRedirected == null)
        {
            ctx.IL.Emit(OpCodes.Call, keyAvailable);
            return;
        }

        var callReal = ctx.IL.Create(OpCodes.Nop);
        var done = ctx.IL.Create(OpCodes.Nop);

        ctx.IL.Emit(OpCodes.Call, isInputRedirected);
        ctx.IL.Emit(OpCodes.Brfalse, callReal);
        ctx.IL.Emit(OpCodes.Ldc_I4_0);
        ctx.IL.Emit(OpCodes.Br, done);
        ctx.IL.Append(callReal);
        ctx.IL.Emit(OpCodes.Call, keyAvailable);
        ctx.IL.Append(done);
    }

    /// <summary>
    /// Emits <c>(int)Console.ReadKey(intercept: true).Key</c>.
    /// </summary>
    /// <remarks>
    /// <c>ConsoleKeyInfo</c> is a value type, so reading <c>Key</c> needs the struct in a local
    /// and a <c>ldloca</c> to take its address — an instance property on a value type cannot be
    /// called against a value sitting on the stack.
    /// </remarks>
    private static void EmitReadKey(IntrinsicContext ctx)
    {
        var keyInfoType = ctx.Type("System.ConsoleKeyInfo");
        var readKey = ctx.Method("System.Console", "ReadKey", "System.Boolean");
        var getKey = ctx.Method("System.ConsoleKeyInfo", "get_Key");

        if (keyInfoType == null || readKey == null || getKey == null)
        {
            return;
        }

        var temp = ctx.DeclareTemp(keyInfoType);

        ctx.IL.Emit(OpCodes.Ldc_I4_1);          // intercept: true — do not echo the keypress
        ctx.IL.Emit(OpCodes.Call, readKey);
        ctx.IL.Emit(OpCodes.Stloc, temp);
        ctx.IL.Emit(OpCodes.Ldloca, temp);
        ctx.IL.Emit(OpCodes.Call, getKey);
        ctx.IL.Emit(OpCodes.Conv_I4);           // ConsoleKey is an enum; ProLang sees a plain int
    }

    /// <summary>
    /// Emits <c>Random.Shared.Next(maxValue)</c>.
    /// </summary>
    /// <remarks>
    /// <c>maxValue</c> is already on the stack but the receiver has to go underneath it, so the
    /// argument is stashed in a local, the shared instance pushed, and the argument reloaded.
    /// </remarks>
    private static void EmitRandom(IntrinsicContext ctx)
    {
        var int32Type = ctx.Type("System.Int32");
        var getShared = ctx.Method("System.Random", "get_Shared");
        var next = ctx.Method("System.Random", "Next", "System.Int32");

        if (int32Type == null || getShared == null || next == null)
        {
            return;
        }

        var maxValue = ctx.DeclareTemp(int32Type);

        ctx.IL.Emit(OpCodes.Stloc, maxValue);
        ctx.IL.Emit(OpCodes.Call, getShared);
        ctx.IL.Emit(OpCodes.Ldloc, maxValue);
        ctx.IL.Emit(OpCodes.Callvirt, next);
    }

    /// <summary>
    /// Emits <c>Convert.ToString((object)str[index])</c>.
    /// </summary>
    /// <remarks>
    /// ProLang has no char type, so <c>charAt</c> yields a one-character string. The char is
    /// boxed and passed through <c>Convert.ToString(object)</c> rather than calling
    /// <c>Char.ToString()</c>, which would need the char's address in a local.
    /// </remarks>
    private static void EmitCharAt(IntrinsicContext ctx)
    {
        var getChars = ctx.Method("System.String", "get_Chars", "System.Int32");
        var charType = ctx.Type("System.Char");
        var convertToString = ctx.Method("System.Convert", "ToString", "System.Object");

        if (getChars == null || charType == null || convertToString == null)
        {
            return;
        }

        ctx.IL.Emit(OpCodes.Callvirt, getChars);
        ctx.IL.Emit(OpCodes.Box, charType);
        ctx.IL.Emit(OpCodes.Call, convertToString);
    }

    /// <summary>
    /// Emits <c>str.Substring(start, end - start)</c>.
    /// </summary>
    /// <remarks>
    /// ProLang's <c>substring(str, start, end)</c> takes an exclusive end index; .NET's takes a
    /// length. Both indices are already on the stack above the receiver, so they are popped into
    /// locals, then start and the computed length are pushed back in the order .NET expects.
    /// </remarks>
    private static void EmitSubstring(IntrinsicContext ctx)
    {
        var substring = ctx.Method("System.String", "Substring", "System.Int32", "System.Int32");

        if (substring == null)
        {
            return;
        }

        var intType = ctx.GetTypeReference(TypeSymbol.Int);
        var end = ctx.DeclareTemp(intType);
        var start = ctx.DeclareTemp(intType);

        // Stack: [str, start, end] — pop in reverse.
        ctx.IL.Emit(OpCodes.Stloc, end);
        ctx.IL.Emit(OpCodes.Stloc, start);
        ctx.IL.Emit(OpCodes.Ldloc, start);
        ctx.IL.Emit(OpCodes.Ldloc, end);
        ctx.IL.Emit(OpCodes.Ldloc, start);
        ctx.IL.Emit(OpCodes.Sub);
        ctx.IL.Emit(OpCodes.Callvirt, substring);
    }
}
