using ProLang.Lsp.Workspace;
using ProLang.Parse;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// The compiler's diagnostics, as the editor's squiggles.
/// </summary>
/// <remarks>
/// The whole of what the old server reported was a regular expression matching any character
/// outside printable ASCII, called an "Invalid character". ProLang has no such rule — the standard
/// library is full of box-drawing characters and the test suite prints ✓ — so what it produced was
/// a red underline under every correct file and nothing at all under a broken one. These are the
/// real diagnostics, from the real binder.
/// </remarks>
internal static class DiagnosticsHandler
{
    public static List<Protocol.Diagnostic> ForDocument(Analysis analysis)
    {
        var diagnostics = new List<Protocol.Diagnostic>();

        if (analysis.Compilation == null)
        {
            // Binding could not run at all; the syntax errors are still worth reporting.
            AddAll(diagnostics, analysis.SyntaxTree.Diagnostics, analysis);

            return diagnostics;
        }

        AddAll(diagnostics, analysis.Compilation.GetDiagnostics(), analysis);

        return diagnostics;
    }

    private static void AddAll(List<Protocol.Diagnostic> into, IEnumerable<Diagnostic> diagnostics, Analysis analysis)
    {
        foreach (var diagnostic in diagnostics)
        {
            // A compilation covers the whole import graph, so it reports errors in imported files
            // too. Those belong to the file they are in, not to this one — publishing them here
            // would put a squiggle on an unrelated line of whatever is on screen.
            if (diagnostic.Location.Text != null
                && !string.Equals(diagnostic.Location.FileName, analysis.Text.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            into.Add(new Protocol.Diagnostic
            {
                Range = LspConversions.ToRange(diagnostic.Location),
                Severity = diagnostic.Severity == DiagnosticSeverity.Warning ? 2 : 1,
                Message = diagnostic.Message,
            });
        }
    }
}
