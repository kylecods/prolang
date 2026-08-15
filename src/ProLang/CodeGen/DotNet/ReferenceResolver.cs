using Mono.Cecil;
using ProLang.Parse;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// Resolves BCL and referenced-assembly members to Cecil references that the emitted module
/// can use.
/// </summary>
/// <remarks>
/// <para>
/// Every type or method the emitter needs from outside the program being compiled comes through
/// here. Resolution walks the loaded assemblies in load order and takes the first match, so
/// <c>System.Private.CoreLib</c> being loaded first (see <see cref="ReferenceAssemblyLocator"/>)
/// is what makes core types resolve to their real definitions rather than to a forwarding facade.
/// </para>
/// <para>
/// Successfully resolved references are imported into the target module before being handed back,
/// so callers never have to remember to call <c>ImportReference</c> themselves — forgetting that
/// produces an assembly that fails verification in ways that are painful to trace.
/// </para>
/// <para>
/// Failures are reported to the <see cref="DiagnosticBag"/> and signalled by a
/// <see langword="null"/> return. Callers must act on that: emitting nothing where a call was
/// expected leaves the evaluation stack unbalanced and produces an assembly that only fails when
/// the method is first JIT-compiled.
/// </para>
/// </remarks>
internal sealed class ReferenceResolver
{
    private readonly List<AssemblyDefinition> _assemblies = [];
    private readonly Dictionary<string, TypeReference> _typeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<(string TypeName, string MethodName, string ParameterSignature), MethodReference> _methodCache = [];
    private readonly Dictionary<(TypeReference TypeRef, string MethodName, int ParameterCount), MethodReference> _genericMethodCache = [];
    private readonly ModuleDefinition _targetModule;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>Creates a resolver that imports into <paramref name="targetModule"/>.</summary>
    /// <param name="targetModule">The module being emitted; every reference is imported into it.</param>
    /// <param name="diagnostics">Where resolution failures are reported.</param>
    public ReferenceResolver(ModuleDefinition targetModule, DiagnosticBag diagnostics)
    {
        _targetModule = targetModule;
        _diagnostics = diagnostics;
    }

    /// <summary>The assemblies currently available for resolution, in load order.</summary>
    public IReadOnlyList<AssemblyDefinition> Assemblies => _assemblies;

    /// <summary>
    /// Makes <paramref name="assembly"/> available for resolution, unless an assembly with the
    /// same simple name is already loaded.
    /// </summary>
    public void AddAssembly(AssemblyDefinition assembly)
    {
        foreach (var existing in _assemblies)
        {
            if (existing.Name.Name == assembly.Name.Name)
            {
                return;
            }
        }

        _assemblies.Add(assembly);
    }

    /// <summary>Makes each of <paramref name="assemblies"/> available for resolution.</summary>
    public void AddAssemblies(IEnumerable<AssemblyDefinition> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            AddAssembly(assembly);
        }
    }

    /// <summary>
    /// Reads an assembly from disk and makes it available for resolution.
    /// </summary>
    /// <remarks>Reports <c>InvalidReference</c> and returns false if the file is not an assembly.</remarks>
    public bool AddReferenceFile(string path)
    {
        try
        {
            AddAssembly(AssemblyDefinition.ReadAssembly(path));
            return true;
        }
        catch (BadImageFormatException)
        {
            _diagnostics.ReportInvalidReference(path);
            return false;
        }
    }

    /// <summary>
    /// Finds a type definition by metadata full name, without importing it.
    /// </summary>
    /// <returns>The first matching definition, or <see langword="null"/> if none is loaded.</returns>
    /// <remarks>
    /// Used by the interop path, which needs the definition itself in order to walk its members.
    /// Prefer <see cref="ResolveType"/> anywhere an emittable reference is wanted.
    /// </remarks>
    public TypeDefinition? FindTypeDefinition(string metadataName)
    {
        foreach (var assembly in _assemblies)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    if (type.FullName == metadataName)
                    {
                        return type;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a type by metadata full name, e.g. <c>System.Collections.Generic.List`1</c>.
    /// </summary>
    /// <returns>
    /// An imported reference, or <see langword="null"/> when the type is missing or ambiguous —
    /// a diagnostic is reported in both cases.
    /// </returns>
    public TypeReference? ResolveType(string metadataName)
    {
        if (_typeCache.TryGetValue(metadataName, out var cached))
        {
            return cached;
        }

        var matches = FindAllTypeDefinitions(metadataName);

        switch (matches.Count)
        {
            case 0:
                _diagnostics.ReportRequiredTypeNotFound(null, metadataName);
                return null;

            case 1:
                var imported = _targetModule.ImportReference(matches[0]);
                _typeCache[metadataName] = imported;
                return imported;

            default:
                // Two assemblies both define the type. Picking one silently would make codegen
                // depend on load order, so this is an error rather than a guess.
                _diagnostics.ReportRequiredTypeAmbiguous(null, metadataName, [.. matches]);
                return null;
        }
    }

    /// <summary>
    /// Resolves a type that the emitter cannot proceed without.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type could not be resolved.</exception>
    /// <remarks>
    /// Reserved for types whose absence means the reference assemblies themselves are broken —
    /// <c>System.Object</c>, <c>System.Int32</c> and the like. Anything that can legitimately be
    /// missing should use <see cref="ResolveType"/> and report a diagnostic instead.
    /// </remarks>
    public TypeReference GetRequiredType(string metadataName) =>
        ResolveType(metadataName)
        ?? throw new InvalidOperationException(
            $"Could not resolve required type '{metadataName}'. The .NET reference assemblies " +
            "could not be located or are incomplete.");

    /// <summary>
    /// Resolves a method by declaring type, name, and exact parameter type names.
    /// </summary>
    /// <param name="typeName">Metadata full name of the declaring type.</param>
    /// <param name="methodName">Method name; <c>.ctor</c> for constructors.</param>
    /// <param name="parameterTypeNames">
    /// Metadata full names of the parameters, in order. Matching is exact, so overloads are
    /// distinguished properly rather than by arity.
    /// </param>
    /// <returns>An imported reference, or <see langword="null"/> with a diagnostic reported.</returns>
    public MethodReference? ResolveMethod(string typeName, string methodName, string[] parameterTypeNames)
    {
        // Resolution is by far the hottest thing the backend does, and the emitter asks for the
        // same handful of members once per emitted call site. Caching by signature turns that
        // into one scan per distinct member instead of one per call site.
        var cacheKey = (typeName, methodName, string.Join(',', parameterTypeNames));

        if (_methodCache.TryGetValue(cacheKey, out var cachedMethod))
        {
            return cachedMethod;
        }

        var resolved = ResolveMethodCore(typeName, methodName, parameterTypeNames);

        // Only successes are cached. A failure has reported a diagnostic, and caching it would
        // suppress that diagnostic at every later call site for the same member.
        if (resolved != null)
        {
            _methodCache[cacheKey] = resolved;
        }

        return resolved;
    }

    private MethodReference? ResolveMethodCore(string typeName, string methodName, string[] parameterTypeNames)
    {
        var matches = FindAllTypeDefinitions(typeName);

        if (matches.Count == 0)
        {
            _diagnostics.ReportRequiredTypeNotFound(null, typeName);
            return null;
        }

        if (matches.Count > 1)
        {
            _diagnostics.ReportRequiredTypeAmbiguous(null, typeName, [.. matches]);
            return null;
        }

        foreach (var method in matches[0].Methods)
        {
            if (method.Name != methodName || method.Parameters.Count != parameterTypeNames.Length)
            {
                continue;
            }

            var allMatch = true;

            for (var i = 0; i < parameterTypeNames.Length; i++)
            {
                if (method.Parameters[i].ParameterType.FullName != parameterTypeNames[i])
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                return _targetModule.ImportReference(method);
            }
        }

        _diagnostics.ReportRequiredMethodNotFound(typeName, methodName, parameterTypeNames);
        return null;
    }

    /// <summary>
    /// Resolves a method on a possibly-generic type, specialising it for the instantiation.
    /// </summary>
    /// <remarks>
    /// Matching is by name and parameter count, which is enough for the constructed generics the
    /// emitter uses (<c>List&lt;T&gt;</c>, <c>Dictionary&lt;K,V&gt;</c>) where overloads at the
    /// same arity do not arise. When <paramref name="type"/> is a
    /// <see cref="GenericInstanceType"/>, the returned reference is rebound to that instantiation
    /// so the call site refers to <c>List&lt;int&gt;::Add</c> rather than <c>List&lt;T&gt;::Add</c>.
    /// </remarks>
    public MethodReference GetGenericMethod(TypeReference type, string methodName, int parameterCount)
    {
        var cacheKey = (type, methodName, parameterCount);

        if (_genericMethodCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var typeDefinition = type.Resolve();
        var methodDefinition = typeDefinition.Methods.First(
            m => m.Name == methodName && m.Parameters.Count == parameterCount);

        var methodReference = _targetModule.ImportReference(methodDefinition);

        if (type is GenericInstanceType genericType)
        {
            var specialized = new MethodReference(methodReference.Name, methodReference.ReturnType, genericType)
            {
                HasThis = methodReference.HasThis,
                ExplicitThis = methodReference.ExplicitThis,
                CallingConvention = methodReference.CallingConvention,
            };

            foreach (var parameter in methodReference.Parameters)
            {
                specialized.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            }

            _genericMethodCache[cacheKey] = specialized;
            return specialized;
        }

        _genericMethodCache[cacheKey] = methodReference;
        return methodReference;
    }

    /// <summary>Imports an already-located definition into the target module.</summary>
    public TypeReference Import(TypeReference type) => _targetModule.ImportReference(type);

    /// <inheritdoc cref="Import(TypeReference)"/>
    public MethodReference Import(MethodReference method) => _targetModule.ImportReference(method);

    /// <inheritdoc cref="Import(TypeReference)"/>
    public FieldReference Import(FieldReference field) => _targetModule.ImportReference(field);

    /// <summary>
    /// Collects every loaded definition of <paramref name="metadataName"/>, so that an ambiguous
    /// type can be reported as such rather than silently resolved to whichever loaded first.
    /// </summary>
    private List<TypeDefinition> FindAllTypeDefinitions(string metadataName)
    {
        var matches = new List<TypeDefinition>(capacity: 1);

        foreach (var assembly in _assemblies)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    if (type.FullName == metadataName)
                    {
                        matches.Add(type);
                    }
                }
            }
        }

        return matches;
    }
}
