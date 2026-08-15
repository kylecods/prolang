using Mono.Cecil;
using Mono.Cecil.Cil;
using ProLang.Intermediate;
using ProLang.Symbols;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// The state that belongs to emitting one method body.
/// </summary>
/// <remarks>
/// <para>
/// Locals, labels, and branch fixups are meaningful only within a single method, but they used to
/// live as emitter instance fields cleared at the top of each body. That made the emitter
/// non-reentrant — emitting one method from inside another, or emitting two in parallel, would
/// have silently corrupted both — and made the lifetime of the state impossible to see from a
/// method signature.
/// </para>
/// <para>
/// One of these is created per body and threaded through statement and expression emission, so
/// the lifetime is now explicit and enforced by the compiler rather than by convention.
/// </para>
/// </remarks>
internal sealed class MethodBodyScope
{
    private readonly Dictionary<VariableSymbol, VariableDefinition> _locals = [];
    private readonly Dictionary<BoundLabel, Instruction> _labels = [];
    private readonly List<(BoundLabel Label, Instruction Branch)> _fixups = [];

    /// <summary>Creates a scope for the body of <paramref name="method"/>.</summary>
    public MethodBodyScope(MethodDefinition method)
    {
        Method = method;
        IL = method.Body.GetILProcessor();
    }

    /// <summary>The method being emitted.</summary>
    public MethodDefinition Method { get; }

    /// <summary>The instruction stream for this body.</summary>
    public ILProcessor IL { get; }

    /// <summary>Declares a local for <paramref name="variable"/> and adds it to the body.</summary>
    public VariableDefinition DeclareLocal(VariableSymbol variable, TypeReference type)
    {
        var local = new VariableDefinition(type);

        IL.Body.Variables.Add(local);
        _locals[variable] = local;

        return local;
    }

    /// <summary>
    /// Declares an unnamed local for holding an intermediate value.
    /// </summary>
    /// <remarks>
    /// Used where the evaluation stack cannot express what is needed — building a struct in
    /// place, or duplicating a field-assignment result past a <c>stfld</c>.
    /// </remarks>
    public VariableDefinition DeclareTemporary(TypeReference type)
    {
        var local = new VariableDefinition(type);
        IL.Body.Variables.Add(local);

        return local;
    }

    /// <summary>The local holding <paramref name="variable"/>.</summary>
    /// <exception cref="KeyNotFoundException">
    /// The variable is read before its declaration was emitted, which means the bound tree is
    /// malformed rather than the program being wrong.
    /// </exception>
    public VariableDefinition GetLocal(VariableSymbol variable) => _locals[variable];

    /// <summary>Marks <paramref name="label"/> as resolving to <paramref name="target"/>.</summary>
    public void MarkLabel(BoundLabel label, Instruction target) => _labels[label] = target;

    /// <summary>
    /// Records a branch whose target is not known yet.
    /// </summary>
    /// <remarks>
    /// Lowering turns structured control flow into labels and gotos, and a goto routinely
    /// precedes its label. Branches are emitted with a placeholder operand and patched by
    /// <see cref="PatchBranches"/> once the whole body exists.
    /// </remarks>
    public void RecordFixup(BoundLabel label, Instruction branch) => _fixups.Add((label, branch));

    /// <summary>
    /// Points every recorded branch at its label.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A branch referenced a label that was never marked. This is a compiler bug — lowering
    /// should never produce a goto to a label it did not also emit.
    /// </exception>
    public void PatchBranches()
    {
        foreach (var (label, branch) in _fixups)
        {
            if (!_labels.TryGetValue(label, out var target))
            {
                throw new InvalidOperationException(
                    $"Branch in '{Method.Name}' targets label '{label.Name}', which was never emitted.");
            }

            branch.Operand = target;
        }
    }
}
