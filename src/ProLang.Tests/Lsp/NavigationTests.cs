using ProLang.Lsp.Handlers;
using ProLang.Tests.Lsp.Infrastructure;

namespace ProLang.Tests.Lsp;

/// <summary>
/// Hover, go to definition, references and rename.
/// </summary>
/// <remarks>
/// Each of these existed in the old server as a text search, and each was wrong in a way a text
/// search cannot avoid: it matched identifiers inside comments and strings, it could not tell two
/// same-named locals apart, its ranges were computed with an off-by-one that pointed at the
/// keyword rather than the name, and rename dispatched a request to the client and then threw on
/// the reply.
/// </remarks>
public class NavigationTests
{
    [Fact]
    public void Hover_ShowsTheSignatureAndTheCommentAboveIt()
    {
        var host = new LspTestHost();
        host.Open("""
            // Adds two numbers together.
            func add(a: int, b: int) : int {
                return a + b
            }

            func main() {
                let sum: int = a|dd(1, 2)
            }
            """);

        var hover = NavigationHandlers.Hover(host.Analysis, host.Cursor);

        Assert.Contains("func add(a: int, b: int): int", hover!.Contents.Value);
        Assert.Contains("Adds two numbers together.", hover.Contents.Value);
        Assert.Equal("add", host.TextOf(hover.Range!));
    }

    /// <summary>
    /// A struct hovers as a struct.
    /// </summary>
    /// <remarks>
    /// <see cref="ProLang.Symbols.TypeSymbol"/> overrides <c>ToString</c> to return its bare name,
    /// so rendering a hover through <c>ToString()</c> would show <c>Point</c> where the source says
    /// <c>struct Point</c>.
    /// </remarks>
    [Fact]
    public void Hover_ShowsWhatKindOfDeclarationItIs()
    {
        var host = new LspTestHost();
        host.Open("""
            // A point on the canvas.
            struct Point { x: int; y: int }

            func main() {
                let p: Po|int = Point { x: 1, y: 2 }
            }
            """);

        var hover = NavigationHandlers.Hover(host.Analysis, host.Cursor);

        Assert.Contains("struct Point", hover!.Contents.Value);
        Assert.Contains("A point on the canvas.", hover.Contents.Value);
    }

    /// <summary>Generic parameters and defaults belong in the signature a hover shows.</summary>
    [Fact]
    public void Hover_ShowsTypeParametersAndDefaults()
    {
        var host = new LspTestHost();
        host.Open("""
            func repeat<T>(value: T, times: int = 3) : T { return value }

            func main() {
                let x: int = rep|eat(1)
            }
            """);

        var hover = NavigationHandlers.Hover(host.Analysis, host.Cursor);

        Assert.Contains("func repeat<T>(value: T, times: int = 3): T", hover!.Contents.Value);
    }

    [Fact]
    public void Hover_ShowsBuiltInDocumentationAndWhichImportItNeeds()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                pri|nt("hello")
            }
            """);

        var hover = NavigationHandlers.Hover(host.Analysis, host.Cursor);

        Assert.Contains("standard output", hover!.Contents.Value);
        Assert.Contains("import \"io\"", hover.Contents.Value);
    }

    /// <summary>
    /// Hovering an import is how someone finds out what a library module is for.
    /// </summary>
    [Fact]
    public void Hover_DescribesAnImportedModule()
    {
        var host = new LspTestHost();
        host.Open("""
            import "u|til"

            func main() { }
            """);

        var hover = NavigationHandlers.Hover(host.Analysis, host.Cursor);

        Assert.Contains("integer helpers", hover!.Contents.Value);
    }

    [Fact]
    public void Hover_DescribesABuiltInModule()
    {
        var host = new LspTestHost();
        host.Open("""
            import "ma|th"

            func main() { }
            """);

        var hover = NavigationHandlers.Hover(host.Analysis, host.Cursor);

        Assert.Contains("integer arithmetic", hover!.Contents.Value);
        Assert.Contains("`min`", hover.Contents.Value);
    }

    /// <summary>
    /// The range points at the identifier, not at the keyword that introduces it.
    /// </summary>
    [Fact]
    public void Definition_LandsOnTheName()
    {
        var host = new LspTestHost();
        host.Open("""
            func helper() : int { return 1 }

            func main() {
                let x: int = hel|per()
            }
            """);

        var location = Assert.Single(NavigationHandlers.Definition(host.Analysis, host.Cursor));

        Assert.Equal("helper", host.TextOf(location.Range));
    }

    [Fact]
    public void Definition_CrossesIntoTheStandardLibrary()
    {
        var host = new LspTestHost();
        host.Open("""
            import "util"

            func main() {
                let x: int = util_a|bs(0 - 5)
            }
            """);

        var location = Assert.Single(NavigationHandlers.Definition(host.Analysis, host.Cursor));

        Assert.EndsWith("util.prl", location.Uri);
    }

    /// <summary>Go to definition on the import itself opens the module.</summary>
    [Fact]
    public void Definition_OnAnImportOpensTheModule()
    {
        var host = new LspTestHost();
        host.Open("""
            import "ui/sh|ape"

            func main() { }
            """);

        var location = Assert.Single(NavigationHandlers.Definition(host.Analysis, host.Cursor));

        Assert.EndsWith("shape.prl", location.Uri);
    }

    [Fact]
    public void Definition_OnABuiltInReturnsNothingToJumpTo()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                pri|nt("hello")
            }
            """);

        Assert.Empty(NavigationHandlers.Definition(host.Analysis, host.Cursor));
    }

    [Fact]
    public void References_FindsEveryUse()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                let tot|al: int = 0
                total = total + 1
                print(total)
            }
            """);

        var references = NavigationHandlers.References(host.Analysis, host.Cursor, includeDeclaration: true);

        Assert.Equal(4, references.Count);
    }

    /// <summary>
    /// A name inside a comment or a string is not a reference to anything.
    /// </summary>
    [Fact]
    public void References_IgnoresCommentsAndStrings()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                let tot|al: int = 0
                // total is the running total
                print("total")
                print(total)
            }
            """);

        var references = NavigationHandlers.References(host.Analysis, host.Cursor, includeDeclaration: true);

        Assert.Equal(2, references.Count);
    }

    [Fact]
    public void References_WithoutTheDeclarationExcludesIt()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                let tot|al: int = 0
                print(total)
            }
            """);

        var references = NavigationHandlers.References(host.Analysis, host.Cursor, includeDeclaration: false);

        Assert.Single(references);
    }

    [Fact]
    public void Rename_ProducesAnEditForEveryOccurrence()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                let tot|al: int = 0
                total = total + 1
                print(total)
            }
            """);

        var (edit, refusal) = NavigationHandlers.Rename(host.Analysis, host.Cursor, "sum");

        Assert.Null(refusal);
        Assert.Equal(4, Assert.Single(edit!.Changes).Value.Count);
    }

    /// <summary>
    /// A builtin lives in the compiler, so renaming it would be renaming nothing the user owns.
    /// </summary>
    [Fact]
    public void Rename_RefusesABuiltIn()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                pri|nt("hello")
            }
            """);

        var (edit, refusal) = NavigationHandlers.Rename(host.Analysis, host.Cursor, "output");

        Assert.Null(edit);
        Assert.Contains("built into the compiler", refusal);
    }

    /// <summary>
    /// A rename does not reach outside the workspace.
    /// </summary>
    /// <remarks>
    /// A compilation covers the whole import graph, and when the standard library is the copy
    /// installed beside the compiler that graph includes files the user does not own and never
    /// opened. Renaming a library function from a program that merely calls it would silently
    /// rewrite them.
    /// </remarks>
    [Fact]
    public void Rename_RefusesASymbolDeclaredOutsideTheWorkspace()
    {
        var host = new LspTestHost();
        host.Open("""
            import "util"

            func main() {
                let x: int = util_a|bs(0 - 5)
            }
            """);

        var (edit, refusal) = NavigationHandlers.Rename(host.Analysis, host.Cursor, "absolute");

        Assert.Null(edit);
        Assert.Contains("outside this workspace", refusal);
    }

    [Fact]
    public void Rename_RefusesAKeywordAsTheNewName()
    {
        var host = new LspTestHost();
        host.Open("""
            func main() {
                let tot|al: int = 0
            }
            """);

        var (edit, refusal) = NavigationHandlers.Rename(host.Analysis, host.Cursor, "struct");

        Assert.Null(edit);
        Assert.Contains("not a valid ProLang name", refusal);
    }

    [Fact]
    public void DocumentLinks_PointAtEachImportedFile()
    {
        var host = new LspTestHost();
        host.Open("""
            import "util"
            import "io"
            import "ui/color"

            func main() { }
            """);

        var links = NavigationHandlers.DocumentLinks(host.Analysis);

        // "io" is a builtin module and has no file, so two of the three link.
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.Target!.EndsWith("util.prl"));
        Assert.Contains(links, l => l.Target!.EndsWith("color.prl"));
    }
}
