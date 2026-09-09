using System.Collections.Immutable;

namespace ProLang.Symbols;

public sealed class StructSymbol : TypeSymbol
{
    // Completion state for Fields. A declared struct is created by the binder's first pass with its
    // name only, and completed by the second pass once every struct name is in scope; a generic
    // instantiation carries a thunk instead, because the template it substitutes from may not itself
    // be complete at the moment the instantiation is created. See the Fields remarks.
    private ImmutableArray<StructField> _fields;
    private Func<ImmutableArray<StructField>>? _lazyFields;
    private bool _fieldsCompleted;

    public StructSymbol(string name, ImmutableArray<TypeParameterSymbol> typeParameters, ImmutableArray<StructField> fields) : base(name)
    {
        TypeParameters = typeParameters;
        _fields = fields;
        _fieldsCompleted = true;
    }

    private StructSymbol(string name, ImmutableArray<TypeParameterSymbol> typeParameters) : base(name)
    {
        TypeParameters = typeParameters;
        _fields = ImmutableArray<StructField>.Empty;
    }

    /// <summary>
    /// Creates a struct whose fields are not yet bound, for the binder's declare-then-complete pass.
    /// </summary>
    /// <remarks>
    /// Declaring the name before binding any field type is what lets a struct name itself, name a
    /// struct declared later in the file, or take part in a cycle of mutual references. The symbol is
    /// unusable until <see cref="CompleteFields"/> runs.
    /// </remarks>
    public static StructSymbol Declaring(
        string name,
        ImmutableArray<TypeParameterSymbol> typeParameters,
        bool isReferenceType = false,
        string? documentation = null) =>
        new(name, typeParameters) { IsReferenceType = isReferenceType, Documentation = documentation };

    /// <summary>Supplies the fields of a symbol created by <see cref="Declaring"/>.</summary>
    public void CompleteFields(ImmutableArray<StructField> fields)
    {
        if (_fieldsCompleted)
        {
            throw new InvalidOperationException($"Fields of struct '{Name}' have already been completed.");
        }

        _fields = fields;
        _fieldsCompleted = true;
    }

    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

    /// <summary>The fields in declaration order.</summary>
    /// <remarks>
    /// Reading this before the binder has completed the symbol is a compiler bug, and it throws rather
    /// than returning empty. Returning empty would be invisible: the caller would emit a struct with no
    /// fields, <c>TypeEmitter</c> would cache that definition under the struct's name, and the assembly
    /// would verify, run, and produce wrong answers.
    /// </remarks>
    public ImmutableArray<StructField> Fields
    {
        get
        {
            if (!_fieldsCompleted)
            {
                if (_lazyFields == null)
                {
                    throw new InvalidOperationException(
                        $"Fields of struct '{Name}' were read before the binder completed them.");
                }

                var thunk = _lazyFields;
                _lazyFields = null;
                _fields = thunk();
                _fieldsCompleted = true;
            }

            return _fields;
        }
    }

    public bool IsGeneric => TypeParameters.Length > 0;

    /// <summary>
    /// Whether this type is heap-allocated and referred to, rather than copied by value.
    /// </summary>
    /// <remarks>
    /// A flag rather than a subtype, because nothing in the compiler dispatches on a struct symbol's
    /// runtime type — every site that matches <see cref="StructSymbol"/> wants to keep matching for
    /// both kinds, and a subtype would only add a case to each.
    /// <para>
    /// It is deliberately <b>not</b> part of <see cref="TypeSymbol"/> equality. Two struct symbols
    /// sharing a name always agree on it, because a scope rejects a duplicate type name; including it
    /// would instead let a symbol-keyed cache and a name-keyed one disagree about the same type.
    /// </para>
    /// </remarks>
    public bool IsReferenceType { get; private init; }

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
        var typeArgs = args.ToImmutableArray();
        var template = this;

        var instantiated = new StructSymbol(instantiatedName, ImmutableArray<TypeParameterSymbol>.Empty)
        {
            OriginalGeneric = this,
            TypeArgs = typeArgs,
            // Must carry over. An instantiation that dropped it would be emitted as a value type while
            // its template is a class, silently — TypeEmitter caches definitions by name, so nothing
            // downstream would ever compare the two.
            IsReferenceType = IsReferenceType,
        };

        // Substituted on first read, not here. BindTypeSyntax instantiates while it is still binding
        // field types, so the template's own fields may not be bound yet; substituting eagerly would
        // capture an empty field list for something like `struct A { d: DynArray<Node> }` declared
        // ahead of DynArray.
        instantiated._lazyFields = () =>
        {
            var substitution = new Dictionary<TypeParameterSymbol, TypeSymbol>();
            for (int i = 0; i < template.TypeParameters.Length; i++)
            {
                substitution[template.TypeParameters[i]] = typeArgs[i];
            }

            var instantiatedFields = ImmutableArray.CreateBuilder<StructField>();
            foreach (var field in template.Fields)
            {
                var instantiatedType = SubstituteTypeParameters(field.Type, substitution);
                instantiatedFields.Add(new StructField(field.Name, instantiatedType));
            }

            return instantiatedFields.ToImmutable();
        };

        return instantiated;
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
