using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class Conversion
{
    public static readonly Conversion None = new(exists:false,isIdentity:false,isImplicit:false);

    public static readonly Conversion Identity = new(true, true, true);

    public static readonly Conversion Explicit = new(true, false, false);

    private Conversion(bool exists, bool isIdentity, bool isImplicit)
    {
        Exists = exists;
        IsIdentity = isIdentity;
        IsImplicit = isImplicit;
    }

    public bool Exists { get; }
    
    public bool IsIdentity { get; }
    
    public bool IsImplicit { get; }
    

    public bool IsExplicit => Exists && !IsImplicit;

    public static Conversion Classify(TypeSymbol from, TypeSymbol to)
    {
        if (from == to)
        {
            return Identity;
        }

        if (from == TypeSymbol.Bool || from == TypeSymbol.Int)
        {
            if (to == TypeSymbol.String)
            {
                return Explicit;
            }
            
        }

        if (from == TypeSymbol.String)
        {
            if (to == TypeSymbol.Bool || to == TypeSymbol.Int)
            {
                return Explicit;
            }
        }

        return None;
    }
}