using ProLang.Lsp.Handlers;
using ProLang.Lsp.Protocol;
using ProLang.Tests.Lsp.Infrastructure;
using ProLang.Text;
using LspPosition = ProLang.Lsp.Protocol.Position;
using LspRange = ProLang.Lsp.Protocol.Range;

namespace ProLang.Tests.Lsp;

/// <summary>
/// Signature help, the outline, folding, inlay hints, semantic colouring and formatting.
/// </summary>
public class EditorFeatureTests
{
    [Fact]
    public void SignatureHelp_HighlightsTheArgumentBeingTyped()
    {
        var host = new LspTestHost();
        host.Open("""
            func box(label: string, width: int, pad: int) : string { return label }

            func main() {
                let s: string = box("a", 2, |)
            }
            """);

        var help = SignatureHelpHandler.Help(host.Analysis, host.Cursor);

        Assert.Equal("func box(label: string, width: int, pad: int): string", help!.Signatures[0].Label);
        Assert.Equal(2, help.ActiveParameter);
    }

    /// <summary>
    /// A comma inside a nested call belongs to that call, not to this one.
    /// </summary>
    /// <remarks>
    /// The old server scanned the whole document for the last <c>name(</c> before the cursor, so a
    /// nested call reported the outer function's signature, and its active parameter was always
    /// zero.
    /// </remarks>
    [Fact]
    public void SignatureHelp_CountsArgumentsOfTheInnermostCall()
    {
        var host = new LspTestHost();
        host.Open("""
            import "math"

            func main() {
                let v: int = max(1, min(2, |))
            }
            """);

        var help = SignatureHelpHandler.Help(host.Analysis, host.Cursor);

        Assert.StartsWith("func min", help!.Signatures[0].Label);
        Assert.Equal(1, help.ActiveParameter);
    }

    /// <summary>A named argument selects its parameter whatever position it is written in.</summary>
    [Fact]
    public void SignatureHelp_FollowsANamedArgument()
    {
        var host = new LspTestHost();
        host.Open("""
            func box(label: string, width: int = 10, pad: int = 0) : string { return label }

            func main() {
                let s: string = box("a", pad: |)
            }
            """);

        var help = SignatureHelpHandler.Help(host.Analysis, host.Cursor);

        Assert.Equal(2, help!.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_ShowsDefaultsAndDocumentation()
    {
        var host = new LspTestHost();
        host.Open("""
            // Draws a box.
            func box(label: string, width: int = 10) : string { return label }

            func main() {
                let s: string = box(|)
            }
            """);

        var help = SignatureHelpHandler.Help(host.Analysis, host.Cursor);

        Assert.Contains("width: int = 10", help!.Signatures[0].Label);
        Assert.Contains("Draws a box.", help.Signatures[0].Documentation!.Value);
    }

    /// <summary>
    /// The outline nests, and its ranges select the name rather than the keyword.
    /// </summary>
    [Fact]
    public void DocumentSymbols_NestFieldsUnderStructs()
    {
        var host = new LspTestHost();
        host.Open("""
            struct Point { x: int; y: int }

            enum Colour { Red, Green }

            func main() { }
            """);

        var symbols = SymbolHandlers.DocumentSymbols(host.Analysis);

        Assert.Equal(["Point", "Colour", "main"], symbols.Select(s => s.Name));
        Assert.Equal(["x", "y"], symbols[0].Children!.Select(c => c.Name));
        Assert.Equal(["Red", "Green"], symbols[1].Children!.Select(c => c.Name));

        Assert.Equal("Point", host.TextOf(symbols[0].SelectionRange));
    }

    [Fact]
    public void WorkspaceSymbols_FindADeclarationInTheStandardLibrary()
    {
        var results = SymbolHandlers.WorkspaceSymbols(
            "shape_disc", [ProLang.Tests.Infrastructure.TestPaths.StandardLibrary], []);

        var result = Assert.Single(results);

        Assert.Equal("shape_disc", result.Name);
        Assert.EndsWith("shape.prl", result.Location.Uri);
    }

    /// <summary>
    /// A build output directory holds copies of source, not source.
    /// </summary>
    /// <remarks>
    /// The compiler copies the whole standard library into its build output, so scanning the
    /// repository root naively finds three of every library declaration and offers the user a
    /// choice between one real file and two that the next build overwrites.
    /// </remarks>
    [Fact]
    public void WorkspaceSymbols_IgnoreBuildOutput()
    {
        var results = SymbolHandlers.WorkspaceSymbols(
            "shape_disc", [ProLang.Tests.Infrastructure.TestPaths.RepoRoot], []);

        var result = Assert.Single(results);

        Assert.DoesNotContain("/bin/", result.Location.Uri);
        Assert.DoesNotContain("/obj/", result.Location.Uri);
    }

    [Fact]
    public void FoldingRanges_CoverDeclarationsAndImportRuns()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"
            import "util"
            import "math"

            func main() {
                print("hello")
            }
            """);

        var ranges = FoldingHandler.Ranges(host.Analysis);

        Assert.Contains(ranges, r => r.Kind == "imports" && r.StartLine == 0 && r.EndLine == 2);
        Assert.Contains(ranges, r => r.Kind == null && r.StartLine == 4);
    }

    [Fact]
    public void InlayHints_ShowTheTypeOfAnInferredLet()
    {
        var host = new LspTestHost();
        host.Open("""
            func main() {
                let count = 1
                let named: int = 2
            }
            """);

        var whole = new LspRange
        {
            Start = new LspPosition { Line = 0, Character = 0 },
            End = new LspPosition { Line = 99, Character = 0 },
        };

        var hint = Assert.Single(InlayHintHandler.Hints(host.Analysis, whole));

        // Only the inferred one: repeating a type that is already written would be noise.
        Assert.Equal(": int", hint.Label);
        Assert.Equal(1, hint.Position.Line);
    }

    [Fact]
    public void SemanticTokens_ClassifyNamesByWhatTheyResolvedTo()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            struct Point { x: int; y: int }

            func main() {
                let p: Point = Point { x: 1, y: 2 }
                print(p.x)
            }
            """);

        var data = SemanticTokensHandler.Tokens(host.Analysis);

        // Five integers per token, and at least one of each interesting kind.
        Assert.Equal(0, data.Count % 5);
        Assert.NotEmpty(data);

        var types = new HashSet<int>();

        for (var i = 3; i < data.Count; i += 5)
        {
            types.Add(data[i]);
        }

        var names = types.Select(t => SemanticTokensHandler.TokenTypes[t]).ToList();

        Assert.Contains("struct", names);
        Assert.Contains("property", names);
        Assert.Contains("function", names);
        Assert.Contains("variable", names);
    }

    /// <summary>
    /// A closing brace dedents its own line.
    /// </summary>
    /// <remarks>
    /// The formatter this replaces never did, so every <c>}</c> came out one level too deep — and
    /// it recomputed the depth by rescanning every preceding line for every line.
    /// </remarks>
    [Fact]
    public void Formatting_IndentsByBraceDepth()
    {
        var formatted = FormattingHandler.Reindent(
            SourceText.From("""
            func main() {
            let x: int = 1
            if (x > 0) {
            print(x)
            }
            }
            """, "test.prl"),
            new FormattingOptions { TabSize = 4, InsertSpaces = true });

        Assert.Equal("""
            func main() {
                let x: int = 1
                if (x > 0) {
                    print(x)
                }
            }
            """, formatted);
    }

    [Fact]
    public void Formatting_LeavesBlankLinesEmpty()
    {
        var formatted = FormattingHandler.Reindent(
            SourceText.From("func main() {\n\n    let x: int = 1\n}", "test.prl"),
            new FormattingOptions());

        Assert.Equal("func main() {\n\n    let x: int = 1\n}", formatted);
    }

    /// <summary>A brace inside a string or a comment does not move anything.</summary>
    [Fact]
    public void Formatting_IgnoresBracesInStringsAndComments()
    {
        var formatted = FormattingHandler.Reindent(
            SourceText.From("""
            import "io"

            func main() {
            // a } brace in a comment
            print("a { brace in a string")
            }
            """, "test.prl"),
            new FormattingOptions());

        Assert.Equal("""
            import "io"

            func main() {
                // a } brace in a comment
                print("a { brace in a string")
            }
            """, formatted);
    }

    [Fact]
    public void Formatting_IsIdempotent()
    {
        var source = SourceText.From("""
            struct Point { x: int; y: int }

            func main() {
                let p: Point = Point { x: 1, y: 2 }

                if (p.x > 0) {
                    while (p.y > 0) {
                        p.y = p.y - 1
                    }
                }
            }
            """, "test.prl");

        var options = new FormattingOptions();
        var once = FormattingHandler.Reindent(source, options);
        var twice = FormattingHandler.Reindent(SourceText.From(once, "test.prl"), options);

        Assert.Equal(once, twice);
        Assert.Equal(source.ToString(), once);
    }

    [Fact]
    public void Formatting_HonoursTheEditorsTabSettings()
    {
        var source = SourceText.From("func main() {\nlet x: int = 1\n}", "test.prl");

        Assert.Equal("func main() {\n  let x: int = 1\n}",
            FormattingHandler.Reindent(source, new FormattingOptions { TabSize = 2, InsertSpaces = true }));

        Assert.Equal("func main() {\n\tlet x: int = 1\n}",
            FormattingHandler.Reindent(source, new FormattingOptions { InsertSpaces = false }));
    }

    /// <summary>The replacement range ends at the end of the document, not past it.</summary>
    [Fact]
    public void Formatting_ReplacesExactlyTheDocument()
    {
        var host = new LspTestHost();
        host.Open("func main() {\nlet x: int = 1\n}");

        var edit = Assert.Single(FormattingHandler.Format(host.Analysis, new FormattingOptions()));

        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(2, edit.Range.End.Line);
        Assert.Equal(1, edit.Range.End.Character);
    }

    /// <summary>
    /// Formatting does not rewrite the file's line endings.
    /// </summary>
    /// <remarks>
    /// It re-emits every line, so emitting <c>\n</c> unconditionally would turn a one-line
    /// indentation fix into a diff touching every line of every file on Windows.
    /// </remarks>
    [Fact]
    public void Formatting_KeepsTheDocumentsLineEndings()
    {
        var formatted = FormattingHandler.Reindent(
            SourceText.From("func main() {\r\nlet x: int = 1\r\n}", "test.prl"),
            new FormattingOptions());

        Assert.Equal("func main() {\r\n    let x: int = 1\r\n}", formatted);
    }

    [Fact]
    public void Formatting_ReturnsNoEditWhenAlreadyFormatted()
    {
        var host = new LspTestHost();
        host.Open("func main() {\n    let x: int = 1\n}");

        Assert.Empty(FormattingHandler.Format(host.Analysis, new FormattingOptions()));
    }
}
