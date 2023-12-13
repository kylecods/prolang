using ProLang.Parse;

namespace ProLang.Interpreter;

public sealed class EvaluationResult
{
    public EvaluationResult(IReadOnlyList<Diagnostic> diagnostics, object value)
    {
        Diagnostics = diagnostics;
        Value = value;
    }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    
    public object Value { get;  }
}