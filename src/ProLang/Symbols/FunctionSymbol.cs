using System.Collections.Immutable;
using ProLang.Syntax;

namespace ProLang.Symbols;

public class FunctionSymbol : Symbol
{
    public FunctionSymbol(string name, ImmutableArray<ParameterSymbol> parameters, TypeSymbol type,
        FunctionDeclarationSyntax? declaration = null,
        ImmutableArray<TypeParameterSymbol> typeParameters = default) : base(name)
    {
        Parameters = parameters;
        Type = type;
        Declaration = declaration;
        TypeParameters = typeParameters.IsDefault ? ImmutableArray<TypeParameterSymbol>.Empty : typeParameters;
    }

    public override SymbolKind Kind => SymbolKind.Function;
    public FunctionDeclarationSyntax? Declaration { get; }
    public ImmutableArray<ParameterSymbol> Parameters { get; }
    public TypeSymbol Type { get; }
    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }
    public bool IsGeneric => TypeParameters.Length > 0;

    // Back-reference to the generic function this was instantiated from
    public FunctionSymbol? OriginalGeneric { get; private set; }
    // The concrete type arguments used when instantiating
    public ImmutableArray<TypeSymbol> TypeArguments { get; private set; } = ImmutableArray<TypeSymbol>.Empty;

    public FunctionSymbol InstantiateGeneric(params TypeSymbol[] args)
    {
        if (!IsGeneric)
            return this;
        if (args.Length != TypeParameters.Length)
            throw new ArgumentException($"Expected {TypeParameters.Length} type argument(s), got {args.Length}");

        var substitution = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        for (int i = 0; i < TypeParameters.Length; i++)
            substitution[TypeParameters[i].Name] = args[i];

        var concreteParams = Parameters.Select(p =>
            new ParameterSymbol(p.Name, SubstituteType(p.Type, substitution), p.Ordinal)
        ).ToImmutableArray();

        var concreteReturn = SubstituteType(Type, substitution);
        var concreteName = $"{Name}<{string.Join(",", args.Select(a => a.ToString()))}>";

        var instantiated = new FunctionSymbol(concreteName, concreteParams, concreteReturn, Declaration)
        {
            OriginalGeneric = this,
            TypeArguments = args.ToImmutableArray(),
        };
        return instantiated;
    }

    private static TypeSymbol SubstituteType(TypeSymbol type, Dictionary<string, TypeSymbol> substitution)
    {
        if (type is TypeParameterSymbol && substitution.TryGetValue(type.Name, out var concrete))
            return concrete;

        // Struct that was instantiated from a generic template: substitute its type args
        if (type is StructSymbol structSym && structSym.OriginalGeneric != null)
        {
            var substitutedArgs = structSym.TypeArgs.Select(a => SubstituteType(a, substitution)).ToArray();
            return structSym.OriginalGeneric.InstantiateGeneric(substitutedArgs);
        }

        if (type.TypeArguments.Length > 0)
        {
            var substituted = type.TypeArguments.Select(a => SubstituteType(a, substitution)).ToArray();
            return type.WithArgs(substituted);
        }

        return type;
    }
}
