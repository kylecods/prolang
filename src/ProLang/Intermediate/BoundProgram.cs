using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Symbols;

namespace ProLang.Intermediate;

public sealed class BoundProgram
{
    public BoundProgram(
        BoundProgram previous,
        ImmutableArray<Diagnostic> diagnostics,
        FunctionSymbol mainFunction,
        FunctionSymbol scriptFunction,
        ImmutableDictionary<FunctionSymbol, BoundBlockStatement> functions,
        ImmutableArray<StructSymbol> structTypes,
        ImmutableArray<EnumSymbol> enumTypes = default,
        ImmutableDictionary<FunctionSymbol, BoundStatement>? structuredFunctions = null,
        ImmutableArray<VariableSymbol>? globalVariables = null,
        ImmutableArray<BoundStatement>? globalInitializers = null)
    {
        Previous = previous;
        Diagnostics = diagnostics;
        MainFunction = mainFunction;
        ScriptFunction = scriptFunction;
        Functions = functions;
        StructTypes = structTypes;
        EnumTypes = enumTypes.IsDefault ? ImmutableArray<EnumSymbol>.Empty : enumTypes;
        StructuredFunctions = structuredFunctions
            ?? ImmutableDictionary<FunctionSymbol, BoundStatement>.Empty;
        GlobalVariables = globalVariables ?? ImmutableArray<VariableSymbol>.Empty;
        GlobalInitializers = globalInitializers ?? ImmutableArray<BoundStatement>.Empty;
    }

    public BoundProgram Previous { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public FunctionSymbol MainFunction { get; }

    public FunctionSymbol ScriptFunction { get; }

    /// <summary>
    /// Function bodies after lowering: <c>if</c>, <c>while</c>, and <c>for</c> have been rewritten
    /// into labels and conditional gotos, and nested blocks flattened.
    /// </summary>
    /// <remarks>
    /// This is what the MSIL emitter consumes — IL has no structured control flow, so lowering
    /// removes the need for the backend to know about it.
    /// </remarks>
    public ImmutableDictionary<FunctionSymbol,BoundBlockStatement> Functions { get; }

    /// <summary>
    /// The same function bodies before lowering, with structured control flow intact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backends that emit a structured language read from here. C has <c>goto</c>, so the C
    /// transpiler could and did consume <see cref="Functions"/>, but the result was a wall of
    /// labels and jumps that no one could review, debug, or step through — and generated C is
    /// something people actually read, unlike IL.
    /// </para>
    /// <para>
    /// Empty for a program bound before this existed, and for synthetic functions the binder does
    /// not produce a body for. A backend must fall back to <see cref="Functions"/> when a symbol
    /// is missing here.
    /// </para>
    /// </remarks>
    public ImmutableDictionary<FunctionSymbol, BoundStatement> StructuredFunctions { get; }

    public ImmutableArray<StructSymbol> StructTypes { get; }

    public ImmutableArray<EnumSymbol> EnumTypes { get; }

    /// <summary>
    /// The <c>global</c> variables declared at file scope, in declaration order.
    /// </summary>
    public ImmutableArray<VariableSymbol> GlobalVariables { get; }

    /// <summary>
    /// One statement per <c>global</c>, assigning its initializer, in declaration order.
    /// </summary>
    public ImmutableArray<BoundStatement> GlobalInitializers { get; }
}