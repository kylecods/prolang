using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using ProLang.Symbols;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// Maps ProLang types onto Cecil type references, emitting struct definitions as it goes.
/// </summary>
/// <remarks>
/// <para>
/// Type mapping and struct emission are one class because they are mutually recursive: resolving
/// a struct type requires its definition to exist, and emitting a definition requires resolving
/// each field's type — which may itself be a struct not yet emitted. Splitting them would mean a
/// callback in each direction for no gain.
/// </para>
/// <para>
/// Struct definitions are created on demand rather than all up front. The binder monomorphises
/// generic structs, and the concrete instantiations are not in
/// <see cref="Intermediate.BoundProgram.StructTypes"/> — they are discovered while emitting the
/// code that uses them.
/// </para>
/// </remarks>
internal sealed class TypeEmitter
{
    private readonly Dictionary<TypeSymbol, TypeReference> _knownTypes = [];

    /// <summary>
    /// Emitted struct definitions, keyed by name.
    /// </summary>
    /// <remarks>
    /// Keyed by name rather than by symbol so that separately-created symbols for the same
    /// concrete instantiation — <c>DynArray&lt;int&gt;</c> reached from two places — resolve to
    /// one type definition rather than emitting duplicates.
    /// </remarks>
    private readonly Dictionary<string, TypeDefinition> _structTypes = new(StringComparer.Ordinal);

    private readonly ReferenceResolver _references;
    private readonly ModuleDefinition _module;
    private readonly TypeReference _dictionaryType;

    public TypeEmitter(ReferenceResolver references, ModuleDefinition module)
    {
        _references = references;
        _module = module;
        _dictionaryType = references.GetRequiredType("System.Collections.Generic.Dictionary`2");
    }

    /// <summary>
    /// The Cecil reference for <paramref name="type"/>, emitting a struct definition if needed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type has no .NET representation.</exception>
    public TypeReference GetReference(TypeSymbol type)
    {
        if (_knownTypes.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var resolved = Resolve(type);
        _knownTypes.Add(type, resolved);

        return resolved;
    }

    /// <summary>Whether a struct definition has already been emitted under this name.</summary>
    public bool TryGetStruct(string name, out TypeDefinition definition) =>
        _structTypes.TryGetValue(name, out definition!);

    /// <summary>
    /// Emits a type definition for <paramref name="structSymbol"/> and adds it to the module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>struct</c> becomes a value type. <c>SequentialLayout</c> matches what that means: a plain
    /// aggregate of fields in declaration order, with no identity of its own.
    /// </para>
    /// <para>
    /// A <c>class</c> becomes a reference type over <c>System.Object</c>, and drops
    /// <c>SequentialLayout</c> — pinning field order would block the runtime from packing reference
    /// fields, and "no identity of its own" is exactly what a class is not. Both stay
    /// <c>Sealed</c>: ProLang has no inheritance.
    /// </para>
    /// </remarks>
    public TypeDefinition EmitStruct(StructSymbol structSymbol)
    {
        if (_structTypes.TryGetValue(structSymbol.Name, out var existing))
        {
            return existing;
        }

        var isReference = structSymbol.IsReferenceType;

        var attributes = TypeAttributes.Sealed | TypeAttributes.Public;
        if (!isReference)
        {
            attributes |= TypeAttributes.SequentialLayout;
        }

        var typeDef = new TypeDefinition(
            string.Empty,
            structSymbol.Name,
            attributes,
            _references.GetRequiredType(isReference ? "System.Object" : "System.ValueType"));

        // Registered before the fields are added, so that a struct containing itself by
        // reference — or two structs referring to each other — does not recurse forever.
        _structTypes[structSymbol.Name] = typeDef;
        _module.Types.Add(typeDef);

        if (isReference)
        {
            EmitDefaultConstructor(typeDef);
        }

        foreach (var field in structSymbol.Fields)
        {
            typeDef.Fields.Add(new FieldDefinition(field.Name, FieldAttributes.Public, GetReference(field.Type)));
        }

        return typeDef;
    }

    /// <summary>
    /// Emits the parameterless constructor a reference type needs in order to be created.
    /// </summary>
    /// <remarks>
    /// <c>newobj</c> takes a constructor reference, and there is no allocating alternative:
    /// <c>initobj</c> on a class merely nulls a slot. The body is the minimum a verifiable
    /// constructor can be — chain to <c>System.Object</c>'s and return.
    /// </remarks>
    private void EmitDefaultConstructor(TypeDefinition typeDef)
    {
        var objectConstructor = _references.ResolveMethod("System.Object", ".ctor", [])
            ?? throw new InvalidOperationException(
                "Could not resolve 'System.Object..ctor()'. The .NET reference assemblies could not "
                + "be located or are incomplete.");

        var constructor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            _references.GetRequiredType("System.Void"));

        var il = constructor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, objectConstructor);
        il.Emit(OpCodes.Ret);

        typeDef.Methods.Add(constructor);
    }

    /// <summary>The parameterless constructor of an emitted reference type.</summary>
    public static MethodReference GetDefaultConstructor(TypeDefinition typeDef) =>
        typeDef.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 0)
        ?? throw new InvalidOperationException(
            $"Reference type '{typeDef.Name}' was emitted without a parameterless constructor.");

    /// <summary>
    /// Maps a ProLang function type onto a BCL <c>Action</c> or <c>Func</c>.
    /// </summary>
    /// <remarks>
    /// Reusing the BCL delegates rather than emitting a delegate type per signature keeps this to
    /// one method: a bespoke type would need a runtime-provided <c>.ctor</c> and <c>Invoke</c>
    /// declared with the right <c>RuntimeManaged</c> flags, all to describe something
    /// <c>Action&lt;int&gt;</c> already describes.
    /// </remarks>
    private TypeReference ResolveFunctionType(FunctionTypeSymbol type)
    {
        var arity = type.ParameterTypes.Length;

        // The binder caps this at FunctionTypeSymbol.MaxParameterCount, so reaching here means the
        // check was bypassed rather than that a program asked for something reasonable.
        if (arity > FunctionTypeSymbol.MaxParameterCount)
        {
            throw new InvalidOperationException(
                $"Function type '{type.Name}' has more parameters than a BCL delegate covers.");
        }

        if (type.ReturnsVoid)
        {
            if (arity == 0)
            {
                return _references.GetRequiredType("System.Action");
            }

            var actionType = _references.GetRequiredType($"System.Action`{arity}");

            return actionType.MakeGenericInstanceType(
                type.ParameterTypes.Select(GetReference).ToArray());
        }

        // Func puts the return type last, so a 2-argument function is Func`3.
        var funcType = _references.GetRequiredType($"System.Func`{arity + 1}");
        var typeArguments = type.ParameterTypes.Select(GetReference).Append(GetReference(type.ReturnType));

        return funcType.MakeGenericInstanceType(typeArguments.ToArray());
    }

    private TypeReference Resolve(TypeSymbol type)
    {
        // Enums are erased to their underlying integer; members are constant-folded at their
        // use sites, so no enum type is ever emitted.
        if (type is EnumSymbol)
        {
            return _references.GetRequiredType("System.Int32");
        }

        if (type is StructSymbol structType)
        {
            var definition = _structTypes.TryGetValue(structType.Name, out var existing)
                ? existing
                : EmitStruct(structType);

            return _module.ImportReference(definition);
        }

        if (type is FunctionTypeSymbol functionType)
        {
            return ResolveFunctionType(functionType);
        }

        // A value obtained from .NET keeps its real type, so an instance call on it can be
        // emitted against the right declaring type rather than against System.Object.
        if (type is DotNetTypeSymbol dotNetType)
        {
            return _references.ResolveType(dotNetType.ClrType.FullName!)
                // The type is not in the loaded reference set — the value still behaves as `any`
                // does, which is what it was typed as before DotNetTypeSymbol existed.
                ?? _references.GetRequiredType("System.Object");
        }

        if (type.TypeArguments.Length > 0)
        {
            return type.Name switch
            {
                "array" => new ArrayType(GetReference(type.TypeArguments[0])),
                "map" => _dictionaryType.MakeGenericInstanceType(
                    GetReference(type.TypeArguments[0]),
                    GetReference(type.TypeArguments[1])),
                _ => throw new InvalidOperationException(
                    $"Generic type '{type.Name}' has no .NET representation."),
            };
        }

        return type.Name switch
        {
            "any" => _references.GetRequiredType("System.Object"),

            // `null` is not a type anyone writes, but it reaches here whenever a null literal flows
            // into `any` and the emitter asks whether the source is a value type.
            "null" => _references.GetRequiredType("System.Object"),

            "bool" => _references.GetRequiredType("System.Boolean"),
            "int" => _references.GetRequiredType("System.Int32"),
            "uint32" => _references.GetRequiredType("System.UInt32"),
            "int16" => _references.GetRequiredType("System.Int16"),
            "uint16" => _references.GetRequiredType("System.UInt16"),
            "int8" => _references.GetRequiredType("System.SByte"),
            "uint8" => _references.GetRequiredType("System.Byte"),
            "int64" => _references.GetRequiredType("System.Int64"),
            "uint64" => _references.GetRequiredType("System.UInt64"),
            "float32" => _references.GetRequiredType("System.Single"),
            "float64" or "float" => _references.GetRequiredType("System.Double"),
            "string" => _references.GetRequiredType("System.String"),
            "void" => _references.GetRequiredType("System.Void"),

            // Untyped array and map: the element types are unknown, so both erase to object.
            "array" => new ArrayType(_references.GetRequiredType("System.Object")),
            "map" => _dictionaryType.MakeGenericInstanceType(
                _references.GetRequiredType("System.Object"),
                _references.GetRequiredType("System.Object")),

            _ => throw new InvalidOperationException($"Type '{type.Name}' has no .NET representation."),
        };
    }
}
