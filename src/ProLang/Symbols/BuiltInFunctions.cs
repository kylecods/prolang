using System.Collections.Immutable;
using ProLang.Symbols.Modules;

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

    public static readonly FunctionSymbol Push = new("push",
        ImmutableArray.Create(
            new ParameterSymbol("arr", TypeSymbol.Any, 0),
            new ParameterSymbol("value", TypeSymbol.Any, 1)),
        TypeSymbol.Void);

    public static readonly FunctionSymbol Pop = new("pop",
        ImmutableArray.Create(new ParameterSymbol("arr", TypeSymbol.Any, 0)),
        TypeSymbol.Any);

    public static readonly FunctionSymbol GetAt = new("getAt",
        ImmutableArray.Create(
            new ParameterSymbol("arr", TypeSymbol.Any, 0),
            new ParameterSymbol("index", TypeSymbol.Int, 1)),
        TypeSymbol.Any);

    public static readonly FunctionSymbol Length = new("length",
        ImmutableArray.Create(new ParameterSymbol("arr", TypeSymbol.Any, 0)),
        TypeSymbol.Int);

    internal static IEnumerable<FunctionSymbol> GetAll() => BuiltInModule.GetAllFunctions();
}
