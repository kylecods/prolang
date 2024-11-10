using System.Collections.Immutable;
using System.Reflection;

namespace ProLang.Symbols;

internal static class BuiltInFunctions
{
    public static readonly FunctionSymbol Print = new("print",
        ImmutableArray.Create(new ParameterSymbol("text", TypeSymbol.String, 0)), TypeSymbol.Void);

    public static readonly FunctionSymbol ReadInput = new("readInput", ImmutableArray<ParameterSymbol>.Empty,
        TypeSymbol.String);

    public static readonly FunctionSymbol Random = new("random",
        ImmutableArray.Create(new ParameterSymbol("max", TypeSymbol.Int,0)), TypeSymbol.Int);
    
    public static readonly FunctionSymbol Min = new("min",
        ImmutableArray.Create(new ParameterSymbol("arg1", TypeSymbol.Int,0), new ParameterSymbol("arg2", TypeSymbol.Int,1)),
        TypeSymbol.Int);
    public static readonly FunctionSymbol Max = new("max",
        ImmutableArray.Create(new ParameterSymbol("arg1", TypeSymbol.Int, 0), new ParameterSymbol("arg2", TypeSymbol.Int,1)),
        TypeSymbol.Int);

    public static readonly FunctionSymbol FileExists = new("fileExists", ImmutableArray.Create(new ParameterSymbol("path",TypeSymbol.String,0)),TypeSymbol.Bool);
    
    public static readonly FunctionSymbol ReadFile = new("readFile", ImmutableArray.Create(new ParameterSymbol("path",TypeSymbol.String,0)),TypeSymbol.String);

    public static readonly FunctionSymbol WriteFile = new("writeFile",
        [new ParameterSymbol("path", TypeSymbol.String,0), new ParameterSymbol("contents", TypeSymbol.String,0)],
        TypeSymbol.Void);

    internal static IEnumerable<FunctionSymbol> GetAll() => typeof(BuiltInFunctions)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(FunctionSymbol))
        .Select(f => (FunctionSymbol)f.GetValue(null)!);
}