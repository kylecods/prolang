namespace ProLang.Intermediate;
public interface ISymTabEntry {

    void AppendLineNumber(int lineNumber);

    void SetAtrribute(SymTabKey key, object value);

    object GetAtrribute(SymTabKey key);
}