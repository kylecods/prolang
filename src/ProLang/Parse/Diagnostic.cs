using ProLang.Text;

namespace ProLang.Parse;

/// <summary>How much a diagnostic matters.</summary>
/// <remarks>
/// Two levels, because the compiler only ever needs two answers: this program cannot be built, or
/// this program can be built and here is something worth knowing. Anything finer is a distinction
/// nothing in the pipeline acts on.
/// </remarks>
public enum DiagnosticSeverity
{
    Error,
    Warning,
}

public sealed class Diagnostic
{
    public Diagnostic(TextLocation location, string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        Location = location;
        Message = message;
        Severity = severity;
    }

    public TextLocation Location { get; }

    public string Message { get; }

    /// <summary>
    /// Defaults to <see cref="DiagnosticSeverity.Error"/>, which is what every diagnostic in the
    /// compiler was before this existed — so every existing construction site keeps its meaning.
    /// </summary>
    /// <remarks>
    /// It exists for the editor. Without it an unused import has to be reported in the same red as
    /// a type error or not reported at all, and a language server that underlines working code in
    /// red is one people turn off.
    /// </remarks>
    public DiagnosticSeverity Severity { get; }

    public bool IsError => Severity == DiagnosticSeverity.Error;

    public bool IsWarning => Severity == DiagnosticSeverity.Warning;

    public override string ToString() => Message;
}