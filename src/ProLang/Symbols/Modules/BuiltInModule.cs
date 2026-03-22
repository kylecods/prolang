namespace ProLang.Symbols.Modules;

public abstract class BuiltInModule
{
    private static readonly Dictionary<string, BuiltInModule> Registry = new(StringComparer.OrdinalIgnoreCase);

    static BuiltInModule()
    {
        Register(new IOModule());
        Register(new MathModule());
        Register(new FileSystemModule());
    }

    private static void Register(BuiltInModule module)
    {
        Registry[module.Name] = module;
    }

    public abstract string Name { get; }

    public abstract IReadOnlyList<FunctionSymbol> Functions { get; }

    public static bool TryGetModule(string name, out BuiltInModule? module)
    {
        return Registry.TryGetValue(name, out module);
    }

    public static IEnumerable<BuiltInModule> GetAll()
    {
        return Registry.Values;
    }

    public static IEnumerable<FunctionSymbol> GetAllFunctions()
    {
        return Registry.Values.SelectMany(m => m.Functions);
    }
}
