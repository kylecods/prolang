using ProLang.Lsp.Handlers;
using ProLang.Tests.Lsp.Infrastructure;

namespace ProLang.Tests.Lsp;

/// <summary>
/// The squiggles.
/// </summary>
/// <remarks>
/// The old server's only rule was a regular expression matching any character outside printable
/// ASCII, reported as "Invalid character" — so it underlined correct code and stayed silent on
/// broken code. These come from the binder.
/// </remarks>
public class DiagnosticsTests
{
    private static List<ProLang.Lsp.Protocol.Diagnostic> Diagnose(string text)
    {
        var host = new LspTestHost();
        host.Open(text);

        return DiagnosticsHandler.ForDocument(host.Analysis);
    }

    [Fact]
    public void AValidProgram_HasNoDiagnostics()
    {
        Assert.Empty(Diagnose("""
            import "io"

            func main() {
                print("hello")
            }
            """));
    }

    /// <summary>
    /// Non-ASCII text is not an error, and never was.
    /// </summary>
    /// <remarks>
    /// This is the false positive that made the old server actively misleading: the repository's
    /// own test programs print ✓, and every module of the standard library draws section rules
    /// with box-drawing characters, so all of them were reported as full of errors.
    /// </remarks>
    [Fact]
    public void NonAsciiText_IsNotAnError()
    {
        Assert.Empty(Diagnose("""
            import "io"

            // Section ── one
            func main() {
                print("✓ passed — 3 checks")
            }
            """));
    }

    [Fact]
    public void AnUndefinedName_IsReportedWhereItIsWritten()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                print(undefined_thing)
            }
            """);

        var diagnostic = Assert.Single(DiagnosticsHandler.ForDocument(host.Analysis));

        Assert.Contains("undefined_thing", diagnostic.Message);
        Assert.Equal("undefined_thing", host.TextOf(diagnostic.Range));
        Assert.Equal(1, diagnostic.Severity);
    }

    [Fact]
    public void ATypeError_IsReported()
    {
        var diagnostics = Diagnose("""
            func main() {
                let count: int = "not a number"
            }
            """);

        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void AnUnresolvableImport_IsReported()
    {
        var host = new LspTestHost();
        host.Open("""
            import "no_such_module"

            func main() { }
            """);

        var diagnostic = Assert.Single(DiagnosticsHandler.ForDocument(host.Analysis));

        Assert.Contains("no_such_module", diagnostic.Message);
    }

    /// <summary>
    /// An error inside an imported file belongs to that file, not to this one.
    /// </summary>
    /// <remarks>
    /// A compilation covers the whole import graph, so it reports errors from every file in it.
    /// Publishing those against the open document would put a squiggle on whatever line of it
    /// happened to share a number with the real error.
    /// </remarks>
    [Fact]
    public void ErrorsFromImportedFiles_AreNotReportedAgainstThisOne()
    {
        var host = new LspTestHost();
        host.Open("""
            import "util"

            func main() {
                let x: int = util_abs(0 - 5)
            }
            """);

        Assert.Empty(DiagnosticsHandler.ForDocument(host.Analysis));
    }

    [Fact]
    public void ASyntaxError_IsReportedRatherThanCrashing()
    {
        Assert.NotEmpty(Diagnose("""
            func main() {
                let x =
            }
            """));
    }

    /// <summary>
    /// Half-typed input is the normal state of a file in an editor.
    /// </summary>
    [Theory]
    [InlineData("func main() { let p: }")]
    [InlineData("func main() { x. }")]
    [InlineData("struct")]
    [InlineData("import")]
    [InlineData("func main() { foo(")]
    [InlineData("}{")]
    [InlineData("enum { , }")]
    public void MalformedInput_ProducesDiagnosticsAndNotAnException(string text)
    {
        Assert.NotEmpty(Diagnose(text));
    }
}
