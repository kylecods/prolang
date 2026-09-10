using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Tests.Compiler;

/// <summary>
/// <c>null</c> as a value of its own, rather than as the integer 0.
/// </summary>
/// <remarks>
/// <c>null</c> has always been a keyword and has always parsed, but the binder read a literal's
/// value as <c>syntax.Value ?? 0</c>, so every <c>null</c> in the language bound to <c>int 0</c> and
/// emitted <c>ldc.i4.0</c>. That is why std/ui/widget.prl uses <c>-1</c> for "no node" and says so:
/// 0 is a valid node index, and a null-that-is-really-0 would silently have meant the root.
/// </remarks>
public class NullBindingTests
{
    private static ProLangCompilation Compile(string text) =>
        ProLangCompilation.Create(SyntaxTree.Parse(SourceText.From(text, "test.prl")));

    private const string NodeClass = """
        class Node {
            value: int;
            next:  Node;
        }
        """;

    [Fact]
    public void NullIsAssignableToAClass()
    {
        var compilation = Compile($$"""
            {{NodeClass}}

            func main() {
                let n: Node = null
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void AClassFieldCanBeInitializedToNull()
    {
        var compilation = Compile($$"""
            {{NodeClass}}

            func main() {
                let n = Node { value: 1, next: null }
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void AClassCanBeComparedWithNull()
    {
        var compilation = Compile($$"""
            import "io"

            {{NodeClass}}

            func main() {
                let n: Node = null
                if (n == null) { print("empty") }
                if (n != null) { print("full") }
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// An `any` can genuinely hold null, and the JSON parser already relies on it.
    /// </summary>
    /// <remarks>
    /// <c>examples/09-json-parser/json-file-utils.prl</c> writes <c>if (value == null)</c> against an
    /// <c>any</c>. A boxed zero is not reference-equal to null, so this has to be a real comparison
    /// rather than the integer one it used to be.
    /// </remarks>
    [Fact]
    public void AnAnyCanBeComparedWithNull()
    {
        var compilation = Compile("""
            import "io"

            func main() {
                let value: any = null
                if (value == null) { print("empty") }
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void NullIsNotAssignableToAValueStruct()
    {
        var compilation = Compile("""
            struct Point {
                x: int;
            }

            func main() {
                let p: Point = null
            }
            """);

        Assert.NotEmpty(compilation.GetDiagnostics());
    }

    [Fact]
    public void NullIsNotAssignableToAnInt()
    {
        var compilation = Compile("""
            func main() {
                let n: int = null
            }
            """);

        Assert.NotEmpty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// A bare <c>let x = null</c> has no type to infer.
    /// </summary>
    /// <remarks>
    /// Inference would otherwise give <c>x</c> the null type, which nothing converts to and no
    /// backend can represent.
    /// </remarks>
    [Fact]
    public void InferringAVariableTypeFromNull_IsReported()
    {
        var compilation = Compile("""
            func main() {
                let x = null
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("x", diagnostic.Message);
        Assert.Contains("null", diagnostic.Message);
    }

    [Fact]
    public void ComparingAValueTypeWithNull_IsReported()
    {
        var compilation = Compile("""
            import "io"

            func main() {
                let n = 5
                if (n == null) { print("never") }
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("never be null", diagnostic.Message);
    }

    [Fact]
    public void OrderingAClassAgainstNull_IsReported()
    {
        var compilation = Compile($$"""
            import "io"

            {{NodeClass}}

            func main() {
                let n: Node = null
                if (n < null) { print("nonsense") }
            }
            """);

        Assert.NotEmpty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// <c>null</c> stays out of the primitive table, so it cannot be written as a type.
    /// </summary>
    [Fact]
    public void NullCannotBeWrittenAsAType()
    {
        Assert.False(TypeSymbol.Primitives.ContainsKey("null"));

        var compilation = Compile("""
            func main() {
                let x: null = 0
            }
            """);

        Assert.NotEmpty(compilation.GetDiagnostics());
    }
}
