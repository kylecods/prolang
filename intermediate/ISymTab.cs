namespace ProLang.Intermediate;
public interface ISymTab {
    SymTabEntry Enter(string name);

    SymTabEntry LookUp(string name);

    List<SymTabEntry> SortedEntries();
}