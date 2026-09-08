using System.Collections.Immutable;

namespace ProLang.Symbols;

public sealed class StructSymbol : TypeSymbol
{
    public StructSymbol(string name, ImmutableArray<TypeParameterSymbol> typeParameters, ImmutableArray<StructField> fields) : base(name)
    {
        TypeParameters = typeParameters;
        Fields = fields;
        FieldsBound = fields.Length > 0;
    }

    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }
    public ImmutableArray<StructField> Fields { get; private set; }

    /// <summary>
    /// True once the binder has bound this template's fields. Generic templates are declared in
    /// two phases — the symbol first, so every other struct can name it, and its fields after —
    /// and an instantiation created in between defers its field substitution until this turns true.
    /// </summary>
    public bool FieldsBound { get; private set; }

    /// <summary>
    /// Instantiations of this template that were created while the template's fields were still
    /// unbound, with the type arguments they were instantiated over. Flushed by
    /// <see cref="SetFields"/> once the template's fields exist.
    /// </summary>
    internal List<(StructSymbol Instance, ImmutableArray<TypeSymbol> Args)>? PendingInstantiations { get; private set; }

    /// <summary>Binder-only: fills the fields of a template declared in two phases.</summary>
    internal void SetFields(ImmutableArray<StructField> fields)
    {
        Fields = fields;
        FieldsBound = true;

        if (PendingInstantiations == null)
        {
            return;
        }

        // Creation order: an instantiation whose type argument is another pending instantiation
        // of this same template was created after it, so the argument is complete by the time
        // the substitution below reads it.
        foreach (var (instance, args) in PendingInstantiations)
        {
            instance.SetFields(CreateInstantiatedFields(args.ToArray()));
        }
        PendingInstantiations = null;
    }

    public bool IsGeneric => TypeParameters.Length > 0;

    public override SymbolKind Kind => SymbolKind.Struct;

    // Set for concrete instantiations; points back to the generic template.
    public StructSymbol? OriginalGeneric { get; private set; }
    // The type arguments used when instantiating (parallel to OriginalGeneric.TypeParameters).
    public ImmutableArray<TypeSymbol> TypeArgs { get; private set; } = ImmutableArray<TypeSymbol>.Empty;

    public StructSymbol InstantiateGeneric(params TypeSymbol[] args)
    {
        if (!IsGeneric)
        {
            // If this is already a concrete instantiation, re-instantiate from the original
            if (OriginalGeneric != null)
                return OriginalGeneric.InstantiateGeneric(args);
            return this;
        }

        if (args.Length != TypeParameters.Length)
            throw new ArgumentException($"Expected {TypeParameters.Length} type arguments, got {args.Length}");

        var instantiatedName = $"{Name}<{string.Join(", ", args.Select(a => a.Name))}>";
        var instance = new StructSymbol(instantiatedName, ImmutableArray<TypeParameterSymbol>.Empty, ImmutableArray<StructField>.Empty)
        {
            OriginalGeneric = this,
            TypeArgs = args.ToImmutableArray(),
        };

        if (!FieldsBound)
        {
            // The template's fields are not bound yet — this instantiation was created by
            // another struct's field binding, which the binder runs before every template's
            // fields exist. Record it; SetFields completes it.
            (PendingInstantiations ??= new List<(StructSymbol, ImmutableArray<TypeSymbol>)>())
                .Add((instance, args.ToImmutableArray()));
            return instance;
        }

        instance.SetFields(CreateInstantiatedFields(args));
        return instance;
    }

    private ImmutableArray<StructField> CreateInstantiatedFields(TypeSymbol[] args)
    {
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
        return instantiatedFields.ToImmutable();
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