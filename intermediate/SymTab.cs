namespace ProLang.Intermediate;

public class SymTab : ISymTab
{
    private readonly int _nestingLevel;

    private readonly Dictionary<string, SymTabEntry> _tabEntries;

    public int NestingLevel { get { return _nestingLevel; } }

    public SymTab(int nestingLevel){
        _nestingLevel = nestingLevel;
        _tabEntries = new Dictionary<string, SymTabEntry>();
    }
    public SymTabEntry Enter(string name)
    {
        var entry = new SymTabEntry(name, this);

        _tabEntries.Add(name, entry);

        return entry;
    }

    public SymTabEntry LookUp(string name)
    {
        return _tabEntries.GetValueOrDefault(name);
    }

    public List<SymTabEntry> SortedEntries()
    {
        var entries = _tabEntries.Values;

        var enumerator = entries.GetEnumerator();

        var list  = new List<SymTabEntry>(_tabEntries.Count);

        while(enumerator.MoveNext()){
            list.Add(enumerator.Current);
        } 
        return list;
    }
}