using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Symbols;


namespace ProLang.Intermediate;

public sealed class BoundGlobalScope
{
    public BoundGlobalScope(BoundGlobalScope? previous,
            ImmutableArray<Diagnostic> diagnostics,
            FunctionSymbol mainFunction,
            FunctionSymbol scriptFunction,
            ImmutableArray<FunctionSymbol> functions,
            ImmutableArray<VariableSymbol> variables,
            ImmutableArray<BoundStatement> statements,
            ImmutableArray<StructSymbol> structTypes,
            ImmutableHashSet<string>? importedModules = null,
            ImmutableArray<EnumSymbol> enumTypes = default,
            ImmutableArray<VariableSymbol>? globalVariables = null,
            ImmutableArray<BoundStatement>? globalInitializers = null)
    {
        Previous = previous;
        Diagnostics = diagnostics;
        MainFunction = mainFunction;
        ScriptFunction = scriptFunction;
        Functions = functions;
        Variables = variables;
        Statements = statements;
        StructTypes = structTypes;
        ImportedModules = importedModules;
        EnumTypes = enumTypes.IsDefault ? ImmutableArray<EnumSymbol>.Empty : enumTypes;
        GlobalVariables = globalVariables ?? ImmutableArray<VariableSymbol>.Empty;
        GlobalInitializers = globalInitializers ?? ImmutableArray<BoundStatement>.Empty;
    }

    public BoundGlobalScope? Previous { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get;  }

    public FunctionSymbol MainFunction { get; }

    public FunctionSymbol ScriptFunction { get; }

    public ImmutableArray<FunctionSymbol> Functions { get; }

    public ImmutableArray<VariableSymbol> Variables { get; }

    public ImmutableArray<BoundStatement> Statements { get; }

    public ImmutableArray<StructSymbol> StructTypes { get; }

    public ImmutableArray<EnumSymbol> EnumTypes { get; }

    public ImmutableHashSet<string>? ImportedModules { get; }

    /// <summary>
    /// The <c>global</c> variables declared at file scope, in declaration order.
    /// </summary>
    public ImmutableArray<VariableSymbol> GlobalVariables { get; }

    /// <summary>
    /// One statement per <c>global</c>, assigning its initializer, in declaration order.
    /// </summary>
    /// <remarks>
    /// Backends run these once before user code: the .NET backend calls
    /// <see cref="SyntheticNames.GlobalsInit"/> from its entry point (or a static constructor for
    /// libraries), and the C backends call it at the top of <c>__UserMain</c>.
    /// </remarks>
    public ImmutableArray<BoundStatement> GlobalInitializers { get; }
}