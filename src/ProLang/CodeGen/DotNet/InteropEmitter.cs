using Mono.Cecil;
using Mono.Cecil.Cil;
using ProLang.Interop;
using ProLang.Symbols;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// Emits calls into .NET assemblies that a ProLang program imported.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam between the compiler's two reflection stacks. The binder discovers interop
/// members with <c>System.Reflection</c> (see <see cref="DotNetAssemblyRegistry"/>) and records
/// the <see cref="System.Reflection.MethodInfo"/> on a <see cref="DotNetFunctionSymbol"/>; the
/// emitter needs a Cecil <see cref="MethodReference"/> to emit a call. Every interop member is
/// therefore resolved twice, the second time by matching metadata full names.
/// </para>
/// <para>
/// Unifying the two stacks would be a much larger change than this refactor; keeping the
/// conversion in one class at least makes the cost visible and gives it somewhere to live.
/// </para>
/// </remarks>
internal sealed class InteropEmitter
{
    private readonly ReferenceResolver _references;

    public InteropEmitter(ReferenceResolver references) => _references = references;

    /// <summary>
    /// Emits a call to an interop member, with any arguments already on the stack.
    /// </summary>
    /// <remarks>
    /// The symbol may stand for a constructor, a method, a static field, or a static property.
    /// They are checked in that order, matching how the binder populates the symbol.
    /// </remarks>
    public void EmitCall(ILProcessor il, DotNetFunctionSymbol function)
    {
        if (function.ConstructorInfo != null)
        {
            var typeRef = ResolveType(function.DeclaringType);
            il.Emit(OpCodes.Newobj, ResolveConstructor(function.ConstructorInfo, typeRef));
            BoxIfErasedToAny(il, function, function.DeclaringType.IsValueType, typeRef);

            return;
        }

        if (function.MethodInfo != null)
        {
            var typeRef = ResolveType(function.DeclaringType);
            var methodRef = ResolveMethod(function.MethodInfo, typeRef);

            il.Emit(function.IsStatic ? OpCodes.Call : OpCodes.Callvirt, methodRef);

            if (function.Type == TypeSymbol.Any && function.MethodInfo.ReturnType.IsValueType)
            {
                il.Emit(OpCodes.Box, ResolveType(function.MethodInfo.ReturnType));
            }

            return;
        }

        // Symbols for fields and properties carry a qualified name such as "Math.PI".
        var memberName = function.Name.Contains('.')
            ? function.Name[(function.Name.LastIndexOf('.') + 1)..]
            : function.Name;

        var field = function.DeclaringType.GetField(memberName);

        if (field != null)
        {
            var typeRef = ResolveType(function.DeclaringType);
            var fieldRef = new FieldReference(field.Name, ResolveFieldType(field.FieldType), typeRef);

            il.Emit(OpCodes.Ldsfld, _references.Import(fieldRef));

            if (function.Type == TypeSymbol.Any && field.FieldType.IsValueType)
            {
                il.Emit(OpCodes.Box, ResolveType(field.FieldType));
            }

            return;
        }

        var property = function.DeclaringType.GetProperty(memberName);

        if (property?.GetMethod != null)
        {
            var typeRef = ResolveType(function.DeclaringType);

            il.Emit(OpCodes.Call, ResolveMethod(property.GetMethod, typeRef));

            if (function.Type == TypeSymbol.Any && property.PropertyType.IsValueType)
            {
                il.Emit(OpCodes.Box, ResolveType(property.PropertyType));
            }

            return;
        }

        throw new NotSupportedException(
            $"Cannot emit .NET member '{function.Name}' (member: '{memberName}')");
    }

    /// <summary>
    /// Boxes the value just produced when ProLang has erased its type to <c>any</c>.
    /// </summary>
    /// <remarks>
    /// <c>any</c> is <c>System.Object</c>, so a value type has to be boxed before it can be
    /// stored in one.
    /// </remarks>
    private static void BoxIfErasedToAny(
        ILProcessor il,
        DotNetFunctionSymbol function,
        bool producesValueType,
        TypeReference typeRef)
    {
        if (function.Type == TypeSymbol.Any && producesValueType)
        {
            il.Emit(OpCodes.Box, typeRef);
        }
    }

    /// <summary>
    /// Converts a reflection <see cref="Type"/> into a Cecil reference.
    /// </summary>
    /// <exception cref="TypeLoadException">The type is not in any loaded assembly.</exception>
    /// <remarks>
    /// If the type is not already loaded, its defining assembly is read from the location
    /// reflection reports and added to the resolver, so later lookups find it directly.
    /// </remarks>
    public TypeReference ResolveType(Type type)
    {
        var found = _references.FindTypeDefinition(type.FullName!);

        if (found != null)
        {
            return _references.Import(found);
        }

        try
        {
            var location = type.Assembly.Location;

            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                var assembly = AssemblyDefinition.ReadAssembly(location);
                _references.AddAssembly(assembly);

                var loaded = assembly.MainModule.Types.FirstOrDefault(t => t.FullName == type.FullName);

                if (loaded != null)
                {
                    return _references.Import(loaded);
                }
            }
        }
        catch
        {
            // Fall through to the throw below; the exception there names the type, which is more
            // useful than whatever went wrong reading the assembly.
        }

        throw new TypeLoadException($"Cannot resolve .NET type '{type.FullName}' in loaded assemblies");
    }

    /// <summary>
    /// Converts a reflection <see cref="System.Reflection.MethodInfo"/> into a Cecil reference,
    /// searching the declaring type and then its base types.
    /// </summary>
    /// <exception cref="MissingMethodException">No matching method was found.</exception>
    /// <remarks>
    /// <b>Known limitation:</b> matching is by name and parameter <i>count</i>, not parameter
    /// types. Overloads that differ only in parameter types resolve to whichever appears first in
    /// metadata order, which can silently emit a call to the wrong one. Fixing this needs a
    /// reflection-to-Cecil comparison for arbitrary parameter types, which
    /// <see cref="ResolveFieldType"/> only covers for a handful of primitives today.
    /// </remarks>
    public MethodReference ResolveMethod(System.Reflection.MethodInfo method, TypeReference typeRef)
    {
        // Resolve against the loaded assemblies rather than typeRef.Resolve(), which would go
        // through Cecil's own assembly resolver and can pick up a different copy of the assembly.
        var typeDef = _references.FindTypeDefinition(typeRef.FullName)
            ?? throw new TypeLoadException($"Cannot resolve type '{typeRef.FullName}' in loaded assemblies");

        var parameterCount = method.GetParameters().Length;
        TypeDefinition? current = typeDef;

        while (current != null)
        {
            var methodDef = current.Methods.FirstOrDefault(
                m => m.Name == method.Name && m.Parameters.Count == parameterCount);

            if (methodDef != null)
            {
                if (typeRef is not GenericInstanceType genericType)
                {
                    return _references.Import(methodDef);
                }

                // Rebind onto the instantiation so the call site names List<int>::Add rather
                // than the open List<T>::Add.
                var specialized = new MethodReference(methodDef.Name, methodDef.ReturnType, genericType)
                {
                    HasThis = methodDef.HasThis,
                    ExplicitThis = methodDef.ExplicitThis,
                    CallingConvention = methodDef.CallingConvention,
                };

                foreach (var parameter in methodDef.Parameters)
                {
                    specialized.Parameters.Add(
                        new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
                }

                return _references.Import(specialized);
            }

            current = current.BaseType != null
                ? _references.FindTypeDefinition(current.BaseType.FullName)
                : null;
        }

        throw new MissingMethodException($"Cannot resolve method '{method.Name}' on type '{typeRef.FullName}'");
    }

    /// <summary>
    /// Converts a reflection constructor into a Cecil reference.
    /// </summary>
    /// <exception cref="MissingMethodException">No constructor of matching arity was found.</exception>
    /// <remarks>Matches on arity only, with the same overload caveat as <see cref="ResolveMethod"/>.</remarks>
    public MethodReference ResolveConstructor(System.Reflection.ConstructorInfo constructor, TypeReference typeRef)
    {
        var typeDef = typeRef.Resolve();
        var parameterCount = constructor.GetParameters().Length;

        var ctorDef = typeDef.Methods.FirstOrDefault(
            m => m.IsConstructor && m.Parameters.Count == parameterCount);

        return ctorDef != null
            ? _references.Import(ctorDef)
            : throw new MissingMethodException($"Cannot resolve constructor on type '{typeRef.FullName}'");
    }

    /// <summary>
    /// Resolves a field's type, short-circuiting the primitives through the metadata-name path.
    /// </summary>
    /// <remarks>
    /// The common cases resolve straight out of the reference assemblies; anything else falls
    /// back to <see cref="ResolveType(Type)"/>, which may have to read another assembly.
    /// </remarks>
    private TypeReference ResolveFieldType(Type type) =>
        type.FullName switch
        {
            "System.Void" => _references.GetRequiredType("System.Void"),
            "System.Boolean" => _references.GetRequiredType("System.Boolean"),
            "System.Int32" => _references.GetRequiredType("System.Int32"),
            "System.String" => _references.GetRequiredType("System.String"),
            "System.Object" => _references.GetRequiredType("System.Object"),
            _ => ResolveType(type),
        };
}
