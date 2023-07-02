namespace ProLang.Intermediate;
public class SymTabEntry : ISymTabEntry
{
    private readonly Dictionary<SymTabKey, object> _entries;

    private readonly string _name;

    private readonly List<int> _lineNumbers;

    private readonly SymTab _symTab;

    public string Name { get { return _name; } }

    public SymTab SymTab { get { return _symTab; } }
    
    public SymTabEntry(string name, SymTab symTab)
    {
        _entries = new();

        _name = name;

        _symTab = symTab;
        
        _lineNumbers = new List<int>();
    }

    public void AppendLineNumber(int lineNumber)
    {
        _lineNumbers.Add(lineNumber);
    }

    public object GetAtrribute(SymTabKey key)
    {
        return _entries.GetValueOrDefault(key);
    }

    public void SetAtrribute(SymTabKey key, object value)
    {
        _entries.Add(key, value);
    }
}