using System.Collections.Immutable;

namespace ProLang.Symbols;

/// <summary>
/// The type of a function value: <c>func(int, int) : bool</c>.
/// </summary>
/// <remarks>
/// <para>
/// Function values are references to top-level functions and <b>capture nothing</b>. That
/// restriction is what lets every backend implement them without a garbage collector: the .NET
/// backend binds one to a BCL <c>Action</c>/<c>Func</c> delegate over a null target, and the C and
/// PSP backends use a plain function pointer.
/// </para>
/// <para>
/// The name is structural — two function types with the same signature are the same type — because
/// <see cref="TypeSymbol"/> compares by name and type arguments. Writing the signature into the
/// name is what makes that comparison do the right thing.
/// </para>
/// </remarks>
public sealed class FunctionTypeSymbol : TypeSymbol
{
    /// <summary>The most parameters a function value may take.</summary>
    /// <remarks>
    /// Set by the .NET backend, which binds function values to the BCL <c>Action</c> and
    /// <c>Func</c> families. Those reach 16 and 17, but the limit is kept well below that and
    /// applied in the binder so every backend agrees on what compiles — the C backend has no such
    /// limit of its own, and a program that built on that would stop being portable.
    /// </remarks>
    public const int MaxParameterCount = 8;

    public FunctionTypeSymbol(ImmutableArray<TypeSymbol> parameterTypes, TypeSymbol returnType)
        : base(BuildName(parameterTypes, returnType))
    {
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
    }

    public ImmutableArray<TypeSymbol> ParameterTypes { get; }

    public TypeSymbol ReturnType { get; }

    public bool ReturnsVoid => ReturnType == Void;

    private static string BuildName(ImmutableArray<TypeSymbol> parameterTypes, TypeSymbol returnType)
        => $"func({string.Join(",", parameterTypes.Select(p => p.Name))}):{returnType.Name}";

    /// <summary>
    /// Whether a function with this signature can be used where <paramref name="other"/> is wanted.
    /// </summary>
    /// <remarks>
    /// Exact match, deliberately. Variance would let a handler be passed something it was not
    /// written to accept, and there is no runtime type check in this language to catch it.
    /// </remarks>
    public bool SignatureMatches(FunctionTypeSymbol other)
    {
        if (ParameterTypes.Length != other.ParameterTypes.Length || ReturnType != other.ReturnType)
        {
            return false;
        }

        for (int i = 0; i < ParameterTypes.Length; i++)
        {
            if (ParameterTypes[i] != other.ParameterTypes[i])
            {
                return false;
            }
        }

        return true;
    }
}
