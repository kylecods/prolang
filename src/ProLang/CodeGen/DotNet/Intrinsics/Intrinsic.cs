using Mono.Cecil.Cil;

namespace ProLang.CodeGen.DotNet.Intrinsics;

/// <summary>
/// The .NET member a builtin function maps onto.
/// </summary>
/// <param name="TypeName">Metadata full name of the declaring type.</param>
/// <param name="MethodName">Method name; <c>.ctor</c> for constructors.</param>
/// <param name="ParameterTypeNames">Metadata full names of the parameters, in order.</param>
/// <param name="IsInstanceCall">
/// True when the first argument is the receiver, so the call needs <c>callvirt</c>.
/// </param>
/// <remarks>
/// Describing the target as data rather than burying it in a closure lets more than one backend
/// read it. The C# rendering backend previously kept its own hand-written copy of this mapping,
/// which could drift out of step with what the MSIL emitter actually emitted.
/// </remarks>
internal sealed record IntrinsicTarget(
    string TypeName,
    string MethodName,
    string[] ParameterTypeNames,
    bool IsInstanceCall = false)
{
    /// <summary>The opcode a call to this member needs.</summary>
    public OpCode CallOpCode => IsInstanceCall ? OpCodes.Callvirt : OpCodes.Call;

    /// <summary>
    /// The target rendered as a C# expression prefix, e.g. <c>ProLang.Runtime.ConsoleOps.Write</c>.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a static call. An instance call takes its receiver from the first
    /// argument, so a backend has to build that expression itself.
    /// </remarks>
    public string QualifiedName => $"{TypeName}.{MethodName}";
}

/// <summary>
/// How a builtin is emitted: either a plain call to a known member, or a custom IL sequence.
/// </summary>
/// <remarks>
/// Almost every builtin is a call, now that the work lives in <c>ProLang.Runtime</c>. The custom
/// form exists for the few where there is genuinely nothing to call — <c>length</c> on an array
/// is <c>ldlen</c> plus <c>conv.i4</c>.
/// </remarks>
internal sealed class Intrinsic
{
    private Intrinsic(IntrinsicTarget? target, IntrinsicEmitter? custom)
    {
        Target = target;
        Custom = custom;
    }

    /// <summary>The member to call, or <see langword="null"/> when <see cref="Custom"/> applies.</summary>
    public IntrinsicTarget? Target { get; }

    /// <summary>A bespoke IL sequence, or <see langword="null"/> when <see cref="Target"/> applies.</summary>
    public IntrinsicEmitter? Custom { get; }

    /// <summary>A builtin that compiles to a static call.</summary>
    public static Intrinsic Call(string typeName, string methodName, params string[] parameterTypeNames) =>
        new(new IntrinsicTarget(typeName, methodName, parameterTypeNames), custom: null);

    /// <summary>A builtin that compiles to an instance call, with the receiver as the first argument.</summary>
    public static Intrinsic CallVirt(string typeName, string methodName, params string[] parameterTypeNames) =>
        new(new IntrinsicTarget(typeName, methodName, parameterTypeNames, IsInstanceCall: true), custom: null);

    /// <summary>A builtin with no member to call, emitted as a bespoke IL sequence.</summary>
    public static Intrinsic Inline(IntrinsicEmitter emitter) => new(target: null, emitter);

    /// <summary>Emits this builtin, with its arguments already on the evaluation stack.</summary>
    public void Emit(IntrinsicContext context)
    {
        if (Custom != null)
        {
            Custom(context);
            return;
        }

        var target = Target!;
        var method = context.Method(target.TypeName, target.MethodName, target.ParameterTypeNames);

        if (method == null)
        {
            // Resolution already reported the failure. Emitting nothing further is deliberate:
            // a partial sequence would strand the arguments on the stack and produce an assembly
            // that only fails when the JIT reaches it. The diagnostic stops the write instead.
            return;
        }

        context.IL.Emit(target.CallOpCode, method);
    }
}
