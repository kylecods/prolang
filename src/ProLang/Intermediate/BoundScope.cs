using System.Collections.Immutable;
using ProLang.Symbols;

namespace ProLang.Intermediate;

internal sealed class BoundScope
{
    private Dictionary<string, VariableSymbol>? _variables;
    private Dictionary<string, FunctionSymbol>? _functions;
    private Dictionary<string, StructSymbol>? _types;
    private Dictionary<string, TypeSymbol>? _typeSymbols;

    public BoundScope(BoundScope? parent)
    {
        Parent = parent;
    }

    public BoundScope? Parent { get; }

    public bool TryDeclareVariable(VariableSymbol variable)
    {
        if (_variables == null)
        {
            _variables = new Dictionary<string, VariableSymbol>();
        }

        if (_variables.ContainsKey(variable.Name))
        {
            return false;
        }
        _variables.Add(variable.Name, variable);
        
        return true;
    }

    public bool TryLookupVariable(string name, out VariableSymbol? variable)
    {
        variable = null;

        if (_variables != null && _variables.TryGetValue(name, out variable))
        {
            return true;
        }

        if (Parent == null)
        {
            return false;
        }

        return Parent.TryLookupVariable(name, out variable);
    }

    public bool TryDeclareFunction(FunctionSymbol function)
    {
        if (_functions == null)
        {
            _functions = new Dictionary<string, FunctionSymbol>();
        }

        if (_functions.ContainsKey(function.Name))
        {
            return false;
        }
        
        _functions.Add(function.Name, function);

        return true;
    }

    public bool TryLookupFunction(string name, out FunctionSymbol? function)
    {
        function = null;

        if (_functions != null && _functions.TryGetValue(name, out function))
        {
            return true;
        }

        if (Parent == null)
        {
            return false;
        }

        return Parent.TryLookupFunction(name, out function);
    }

    public bool TryDeclareType(StructSymbol type)
    {
        if (_types == null)
        {
            _types = new Dictionary<string, StructSymbol>();
        }

        if (_types.ContainsKey(type.Name))
        {
            return false;
        }
        
        _types.Add(type.Name, type);

        return true;
    }

    public bool TryLookupType(string name, out StructSymbol? type)
    {
        type = null;

        if (_types != null && _types.TryGetValue(name, out type))
        {
            return true;
        }

        if (Parent == null)
        {
            return false;
        }

        return Parent.TryLookupType(name, out type);
    }

    public bool TryDeclareTypeSymbol(TypeSymbol typeSymbol)
    {
        if (_typeSymbols == null)
        {
            _typeSymbols = new Dictionary<string, TypeSymbol>();
        }

        if (_typeSymbols.ContainsKey(typeSymbol.Name))
        {
            return false;
        }

        _typeSymbols.Add(typeSymbol.Name, typeSymbol);
        return true;
    }

    // Declares a type alias: alias name → concrete TypeSymbol (used for generic instantiation).
    public void DeclareTypeBinding(string alias, TypeSymbol concrete)
    {
        if (_typeSymbols == null)
            _typeSymbols = new Dictionary<string, TypeSymbol>();
        _typeSymbols[alias] = concrete;
    }

    public bool TryLookupTypeSymbol(string name, out TypeSymbol? typeSymbol)
    {
        typeSymbol = null;

        if (_typeSymbols != null && _typeSymbols.TryGetValue(name, out typeSymbol))
        {
            return true;
        }

        if (Parent == null)
        {
            return false;
        }

        return Parent.TryLookupTypeSymbol(name, out typeSymbol);
    }

    public ImmutableArray<VariableSymbol> GetDeclaredVariables()
    {
        if (_variables == null)
        {
            return ImmutableArray<VariableSymbol>.Empty;
        }
        return _variables.Values.ToImmutableArray();
    }

    public ImmutableArray<FunctionSymbol> GetDeclaredFunctions()
    {
        if (_functions == null)
        {
            return ImmutableArray<FunctionSymbol>.Empty;
        }

        return _functions.Values.ToImmutableArray();
    }

    public ImmutableArray<StructSymbol> GetDeclaredTypes()
    {
        if (_types == null)
        {
            return ImmutableArray<StructSymbol>.Empty;
        }

        return _types.Values.ToImmutableArray();
    }
}