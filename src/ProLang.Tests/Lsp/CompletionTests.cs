using ProLang.Lsp.Handlers;
using ProLang.Tests.Lsp.Infrastructure;

namespace ProLang.Tests.Lsp;

/// <summary>
/// Completion, which the old server had in name only: it returned every keyword, every built-in
/// and every symbol in the file at every position, including inside strings and comments, and
/// handled none of the trigger characters it declared.
/// </summary>
public class CompletionTests
{
    private static List<string> Complete(string markedText)
    {
        var host = new LspTestHost();
        host.Open(markedText);

        return CompletionHandler.Complete(host.Analysis, host.Cursor)
            .Select(i => i.Label)
            .ToList();
    }

    [Fact]
    public void AfterADot_OffersOnlyTheStructsFields()
    {
        var labels = Complete("""
            struct Point { x: int; y: int }

            func main() {
                let p: Point = Point { x: 1, y: 2 }
                let v: int = p.|
            }
            """);

        Assert.Equal(["x", "y"], labels);
    }

    /// <summary>
    /// The receiver is not always a plain name, which is why expression types are recorded.
    /// </summary>
    [Fact]
    public void AfterADot_TypesACallResult()
    {
        var labels = Complete("""
            struct Point { x: int; y: int }

            func origin() : Point { return Point { x: 0, y: 0 } }

            func main() {
                let v: int = origin().|
            }
            """);

        Assert.Equal(["x", "y"], labels);
    }

    [Fact]
    public void AfterADot_OffersStringMethods()
    {
        var labels = Complete("""
            func main() {
                let s: string = "hello"
                let n: int = s.|
            }
            """);

        Assert.Equal(["length", "charAt", "charCode", "substring", "indexOf"], labels);
    }

    [Fact]
    public void AfterADot_OffersArrayLengthAndNothingElse()
    {
        var labels = Complete("""
            func main() {
                let a: array<int> = array_new(4)
                let n: int = a.|
            }
            """);

        Assert.Equal(["length"], labels);
    }

    [Fact]
    public void AfterADot_OffersEnumMembers()
    {
        var labels = Complete("""
            enum Colour { Red, Green = 5 }

            func main() {
                let c: Colour = Colour.|
            }
            """);

        Assert.Equal(["Red", "Green"], labels);
    }

    /// <summary>
    /// The standard library, discoverable at the moment someone is choosing what to import.
    /// </summary>
    [Fact]
    public void InAnImport_OffersBuiltInModulesAndTheStandardLibrary()
    {
        var labels = Complete("""
            import "|"

            func main() { }
            """);

        Assert.Contains("io", labels);
        Assert.Contains("math", labels);
        Assert.Contains("util", labels);
        Assert.Contains("ui/shape", labels);
        Assert.Contains("dotnet:", labels);
    }

    [Fact]
    public void InAnImport_DescribesEachModule()
    {
        var host = new LspTestHost();
        host.Open("""
            import "|"

            func main() { }
            """);

        var items = CompletionHandler.Complete(host.Analysis, host.Cursor);

        var io = items.Single(i => i.Label == "io");
        Assert.Contains("Printing", io.Documentation?.Value);

        var shape = items.Single(i => i.Label == "ui/shape");
        Assert.False(string.IsNullOrWhiteSpace(shape.Documentation?.Value));
    }

    [Fact]
    public void InATypeClause_OffersTypesAndNotVariables()
    {
        var labels = Complete("""
            struct Point { x: int; y: int }

            func main() {
                let count: int = 1
                let p: |
            }
            """);

        Assert.Contains("int", labels);
        Assert.Contains("Point", labels);
        Assert.DoesNotContain("count", labels);
        Assert.DoesNotContain("let", labels);
    }

    /// <summary>
    /// The gap the old list had: <c>uint32</c> was a type symbol the binder could not resolve.
    /// </summary>
    [Fact]
    public void InATypeClause_OffersEveryIntegerWidth()
    {
        var labels = Complete("""
            func main() {
                let x: |
            }
            """);

        foreach (var name in new[] { "int", "int8", "int16", "int64", "uint8", "uint16", "uint32", "uint64" })
        {
            Assert.Contains(name, labels);
        }
    }

    [Fact]
    public void InAnExpression_OffersLocalsFunctionsAndKeywords()
    {
        var labels = Complete("""
            func helper() : int { return 1 }

            func main() {
                let total: int = 0
                |
            }
            """);

        Assert.Contains("total", labels);
        Assert.Contains("helper", labels);
        Assert.Contains("while", labels);
        Assert.Contains("struct", labels);
        Assert.Contains("enum", labels);
        Assert.Contains("as", labels);
        Assert.Contains("null", labels);
    }

    /// <summary>
    /// A builtin of a module this file has not imported comes with the import that would fix it.
    /// </summary>
    [Fact]
    public void AnUnimportedBuiltIn_IsOfferedWithTheImportItNeeds()
    {
        var host = new LspTestHost();
        host.Open("""
            func main() {
                |
            }
            """);

        var print = CompletionHandler.Complete(host.Analysis, host.Cursor)
            .First(i => i.Label == "print");

        Assert.Contains("requires import \"io\"", print.Detail);

        var edit = Assert.Single(print.AdditionalTextEdits!);
        Assert.Equal("import \"io\"\n", edit.NewText);
    }

    [Fact]
    public void AnImportedBuiltIn_IsOfferedWithoutAnEdit()
    {
        var host = new LspTestHost();
        host.Open("""
            import "io"

            func main() {
                |
            }
            """);

        var print = CompletionHandler.Complete(host.Analysis, host.Cursor)
            .First(i => i.Label == "print");

        Assert.Null(print.AdditionalTextEdits);
    }

    [Fact]
    public void InAComment_OffersNothing()
    {
        Assert.Empty(Complete("""
            func main() {
                // a note about | things
            }
            """));
    }

    [Fact]
    public void InAString_OffersNothing()
    {
        Assert.Empty(Complete("""
            import "io"

            func main() {
                print("hello | there")
            }
            """));
    }

    /// <summary>Named arguments, since ProLang has them and they are hard to remember.</summary>
    [Fact]
    public void InsideACall_OffersTheParameterNames()
    {
        var labels = Complete("""
            func box(label: string, width: int = 10, pad: int = 0) : string { return label }

            func main() {
                let s: string = box("a", |)
            }
            """);

        Assert.Contains("width:", labels);
        Assert.Contains("pad:", labels);
    }
}
