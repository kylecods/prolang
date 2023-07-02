namespace ProLang.Intermediate;

public interface ISymTabStack {
    SymTab GetLocalSymTab();

    SymTabEntry EnterLocal(string name);

    SymTabEntry LookUpLocal(string name);

    SymTabEntry LookUp(string name);
}