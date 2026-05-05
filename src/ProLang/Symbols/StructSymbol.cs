using System.Collections.Immutable;

namespace ProLang.Symbols;

public sealed class StructSymbol : TypeSymbol
{
    public StructSymbol(string name, ImmutableArray<TypeParameterSymbol> typeParameters, ImmutableArray<StructField> fields) : base(name)
    {
        TypeParameters = typeParameters;
        Fields = fields;
    }

    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }
    public ImmutableArray<StructField> Fields { get; }

    public bool IsGeneric => TypeParameters.Length > 0;

    public override SymbolKind Kind => SymbolKind.Type;

    public StructSymbol InstantiateGeneric(params TypeSymbol[] args)
    {
        if (!IsGeneric)
            return this;

        if (args.Length != TypeParameters.Length)
            throw new ArgumentException($"Expected {TypeParameters.Length} type arguments, got {args.Length}");

        var substitution = new Dictionary<TypeParameterSymbol, TypeSymbol>();
        for (int i = 0; i < TypeParameters.Length; i++)
        {
            substitution[TypeParameters[i]] = args[i];
        }

        var instantiatedFields = ImmutableArray.CreateBuilder<StructField>();
        foreach (var field in Fields)
        {
            var instantiatedType = SubstituteTypeParameters(field.Type, substitution);
            instantiatedFields.Add(new StructField(field.Name, instantiatedType));
        }

        var instantiatedName = $"{Name}<{string.Join(", ", args.Select(a => a.Name))}>";
        return new StructSymbol(instantiatedName, ImmutableArray<TypeParameterSymbol>.Empty, instantiatedFields.ToImmutable());
    }

    private static TypeSymbol SubstituteTypeParameters(TypeSymbol type, Dictionary<TypeParameterSymbol, TypeSymbol> substitution)
    {
        if (type is TypeParameterSymbol typeParam && substitution.TryGetValue(typeParam, out var concreteType))
        {
            return concreteType;
        }

        if (type.TypeArguments.Length > 0)
        {
            var substitutedArgs = ImmutableArray.CreateBuilder<TypeSymbol>();
            foreach (var arg in type.TypeArguments)
            {
                substitutedArgs.Add(SubstituteTypeParameters(arg, substitution));
            }
            return type.WithArgs(substitutedArgs.ToArray());
        }

        return type;
    }
}