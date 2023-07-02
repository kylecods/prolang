namespace ProLang.Intermediate;

public class SymTabStack : ISymTabStack
{
    private readonly int _currentNestingLevel;

    private readonly List<SymTab> _tabStack;

    public SymTabStack(){
        _currentNestingLevel = 0;
        _tabStack = new List<SymTab>
        {
            new SymTab(_currentNestingLevel)
        };

    }

    public int CurrentNestingLevel => _currentNestingLevel;

    public SymTabEntry EnterLocal(string name)
    {
        return _tabStack.Find(x => x.NestingLevel == _currentNestingLevel).Enter(name);
    }

    public SymTab GetLocalSymTab()
    {
        return _tabStack.Find(x => x.NestingLevel == _currentNestingLevel);
    }

    public SymTabEntry LookUp(string name)
    {
        return LookUpLocal(name);
    }

    public SymTabEntry LookUpLocal(string name)
    {
        return _tabStack.Find(x => x.NestingLevel == _currentNestingLevel).LookUp(name);
    }
}