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
        ImmutableArray<StructSymbol> structTypes)
    {
        Previous = previous;
        Diagnostics = diagnostics;
        MainFunction = mainFunction;
        ScriptFunction = scriptFunction;
        Functions = functions;
        StructTypes = structTypes;
    }

    public BoundProgram Previous { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public FunctionSymbol MainFunction { get; }

    public FunctionSymbol ScriptFunction { get; }

    public ImmutableDictionary<FunctionSymbol,BoundBlockStatement> Functions { get; }

    public ImmutableArray<StructSymbol> StructTypes { get; }
}