using Mono.Cecil;
using Mono.Cecil.Cil;
using ProLang.Parse;
using ProLang.Symbols;

namespace ProLang.CodeGen.DotNet.Intrinsics;

/// <summary>
/// Everything an intrinsic emitter needs in order to emit IL for one builtin call site.
/// </summary>
/// <remarks>
/// Passing a context rather than reaching into emitter fields is what lets the builtin table live
/// outside the emitter. Each intrinsic is handed exactly the capabilities it needs and nothing
/// else, so a new builtin cannot accidentally depend on emitter-wide state.
/// <para>
/// By the time an intrinsic runs, the call's arguments have already been emitted and are on the
/// evaluation stack in declaration order.
/// </para>
/// </remarks>
internal sealed class IntrinsicContext(
    ILProcessor il,
    ReferenceResolver references,
    DiagnosticBag diagnostics,
    Func<TypeSymbol, TypeReference> getTypeReference)
{
    /// <summary>The IL stream for the method being emitted.</summary>
    public ILProcessor IL { get; } = il;

    /// <summary>Resolves BCL members.</summary>
    public ReferenceResolver References { get; } = references;

    /// <summary>Where an intrinsic reports that it could not be emitted.</summary>
    public DiagnosticBag Diagnostics { get; } = diagnostics;

    /// <summary>Maps a ProLang type onto its Cecil reference.</summary>
    public Func<TypeSymbol, TypeReference> GetTypeReference { get; } = getTypeReference;

    /// <summary>
    /// Declares a scratch local in the method being emitted.
    /// </summary>
    /// <remarks>
    /// Some builtins need to reorder or stash stack values — <c>substring</c> converts an
    /// exclusive end index into a length, and <c>random</c> has to get the receiver underneath an
    /// already-pushed argument. Each call site currently gets its own local.
    /// </remarks>
    public VariableDefinition DeclareTemp(TypeReference type)
    {
        var local = new VariableDefinition(type);
        IL.Body.Variables.Add(local);

        return local;
    }

    /// <summary>
    /// Resolves a method, reporting a diagnostic if it is missing.
    /// </summary>
    /// <returns>
    /// The reference, or <see langword="null"/> if it could not be resolved. Callers must not
    /// emit a partial sequence when this returns null.
    /// </returns>
    public MethodReference? Method(string typeName, string methodName, params string[] parameterTypeNames) =>
        References.ResolveMethod(typeName, methodName, parameterTypeNames);

    /// <summary>Resolves a type, reporting a diagnostic if it is missing.</summary>
    public TypeReference? Type(string metadataName) => References.ResolveType(metadataName);
}

/// <summary>
/// Emits the IL for one builtin call site. Arguments are already on the stack.
/// </summary>
internal delegate void IntrinsicEmitter(IntrinsicContext context);
