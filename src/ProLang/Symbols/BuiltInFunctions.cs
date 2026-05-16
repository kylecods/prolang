using System.Collections.Immutable;
using ProLang.Symbols.Modules;

namespace ProLang.Symbols;

internal static class BuiltInFunctions
{
    public static readonly FunctionSymbol Print = new("print",
        ImmutableArray.Create(new ParameterSymbol("text", TypeSymbol.Any, 0)), TypeSymbol.Void);

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

    public static readonly FunctionSymbol ArrayLength = new("length",
        ImmutableArray.Create(new ParameterSymbol("arr", TypeSymbol.Any, 0)),
        TypeSymbol.Int);

    public static readonly FunctionSymbol ArrayNew = new("array_new",
        ImmutableArray.Create(new ParameterSymbol("size", TypeSymbol.Int, 0)),
        TypeSymbol.Array);

    // String methods
    public static readonly FunctionSymbol StringLength = new("length",
        ImmutableArray.Create(new ParameterSymbol("str", TypeSymbol.String, 0)),
        TypeSymbol.Int);

    public static readonly FunctionSymbol StringCharAt = new("charAt",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("index", TypeSymbol.Int, 1)),
        TypeSymbol.String);

    public static readonly FunctionSymbol StringSubstring = new("substring",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("start", TypeSymbol.Int, 1),
            new ParameterSymbol("end", TypeSymbol.Int, 2)),
        TypeSymbol.String);

    public static readonly FunctionSymbol StringIndexOf = new("indexOf",
        ImmutableArray.Create(
            new ParameterSymbol("str", TypeSymbol.String, 0),
            new ParameterSymbol("needle", TypeSymbol.String, 1)),
        TypeSymbol.Int);

    // Console and threading functions
    public static readonly FunctionSymbol ConsoleWrite = new("console_write",
        ImmutableArray.Create(new ParameterSymbol("text", TypeSymbol.String, 0)), TypeSymbol.Void);

    public static readonly FunctionSymbol ConsoleSetCursor = new("console_set_cursor",
        ImmutableArray.Create(
            new ParameterSymbol("x", TypeSymbol.Int, 0),
            new ParameterSymbol("y", TypeSymbol.Int, 1)),
        TypeSymbol.Void);

    public static readonly FunctionSymbol ConsoleHideCursor = new("console_hide_cursor",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void);

    public static readonly FunctionSymbol ConsoleSetColor = new("console_set_color",
        ImmutableArray.Create(new ParameterSymbol("color", TypeSymbol.Int, 0)), TypeSymbol.Void);

    public static readonly FunctionSymbol ConsoleResetColor = new("console_reset_color",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Void);

    public static readonly FunctionSymbol ConsoleKeyAvailable = new("console_key_available",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Bool);

    public static readonly FunctionSymbol ConsoleReadKey = new("console_read_key",
        ImmutableArray<ParameterSymbol>.Empty, TypeSymbol.Int);

    public static readonly FunctionSymbol ThreadSleep = new("thread_sleep",
        ImmutableArray.Create(new ParameterSymbol("ms", TypeSymbol.Int, 0)), TypeSymbol.Void);

    internal static IEnumerable<FunctionSymbol> GetAll() => BuiltInModule.GetAllFunctions();
}
