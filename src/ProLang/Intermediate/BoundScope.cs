using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundScope
{
    private Dictionary<string, VariableSymbol> _variables = new ();

    public BoundScope(BoundScope parent)
    {
        Parent = parent;
    }

    public BoundScope Parent { get; }

    public bool TryDeclare(VariableSymbol variable)
    {
        return _variables.TryAdd(variable.Name, variable);
    }

    public bool TryLookup(string name, out VariableSymbol variable)
    {
        if (_variables.TryGetValue(name, out variable))
        {
            return true;
        }

        if (Parent == null)
        {
            return false;
        }

        return Parent.TryLookup(name, out variable);
    }

    public ImmutableArray<VariableSymbol> GetDeclaredVariables()
    {
        return _variables.Values.ToImmutableArray();
    }
}