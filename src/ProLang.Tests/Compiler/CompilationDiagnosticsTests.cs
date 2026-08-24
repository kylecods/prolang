using System.Reflection;
using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Tests.Compiler;

/// <summary>
/// The diagnostic surface a language server consumes, and the primitive-type table it shares with
/// the binder.
/// </summary>
public class CompilationDiagnosticsTests
{
    private static ProLangCompilation Compile(string text) =>
        ProLangCompilation.Create(SyntaxTree.Parse(SourceText.From(text, "test.prl")));

    /// <summary>
    /// The one integer width that would not bind.
    /// </summary>
    /// <remarks>
    /// <c>TypeSymbol.UInt32</c> has always existed and the .NET, C and C# backends all emit it,
    /// but the binder's name-resolution switch had no case for it, so this program reported
    /// "Type 'uint32' doesn't exist". Both now read <see cref="TypeSymbol.Primitives"/>.
    /// </remarks>
    [Theory]
    [InlineData("uint32")]
    [InlineData("int")]
    [InlineData("int8")]
    [InlineData("uint8")]
    [InlineData("int16")]
    [InlineData("uint16")]
    [InlineData("int64")]
    [InlineData("uint64")]
    public void EveryIntegerWidth_BindsInATypeClause(string typeName)
    {
        var compilation = Compile($$"""
            func main() {
                let value: {{typeName}} = 7
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// Every well-known type a program can name is in the shared table.
    /// </summary>
    /// <remarks>
    /// This is the drift guard. The gap above existed because the list of primitive symbols and
    /// the list of names the binder accepted were two separate lists, and one of them was missed.
    /// Adding a <see cref="TypeSymbol"/> without adding it here now fails.
    /// <see cref="TypeSymbol.Error"/> and <see cref="TypeSymbol.Null"/> are excluded on purpose —
    /// neither can be written in source.
    /// </remarks>
    [Fact]
    public void Primitives_CoverEveryWellKnownType()
    {
        var wellKnown = typeof(TypeSymbol)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(TypeSymbol))
            .Select(f => (TypeSymbol)f.GetValue(null)!)
            .Where(t => t != TypeSymbol.Error && t != TypeSymbol.Null)
            .Select(t => t.Name);

        foreach (var name in wellKnown)
        {
            Assert.True(
                TypeSymbol.Primitives.ContainsKey(name),
                $"TypeSymbol.{name} is not in TypeSymbol.Primitives, so the binder cannot resolve it "
                + "and completion cannot offer it.");
        }
    }

    [Fact]
    public void GetDiagnostics_ReportsAnUnresolvedImportOnItsOwn()
    {
        var compilation = Compile("""
            import "no_such_module"

            func main() {
                print(undefined_name)
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("no_such_module", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_ReportsBinderErrors()
    {
        var compilation = Compile("""
            import "io"

            func main() {
                print(undefined_name)
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("undefined_name", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_IsEmptyForAValidProgram()
    {
        var compilation = Compile("""
            import "io"

            struct Point { x: int; y: int }

            enum Colour { Red, Green = 5 }

            func describe(p: Point, label: string = "point") : string {
                return label + " " + p.x + "," + p.y
            }

            func main() {
                let p: Point = Point { x: 1, y: 2 }
                print(describe(p, label: "origin"))
                print(Colour.Green)
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// Half-typed input reports rather than throws.
    /// </summary>
    /// <remarks>
    /// The binder throws outright on a few unexpected syntax shapes. A compiler about to exit can
    /// afford that; a language server answering the next keystroke cannot, so binding is guarded
    /// and degrades to the syntax diagnostics.
    /// </remarks>
    [Theory]
    [InlineData("func main() { let x = }")]
    [InlineData("func main() { if ( }")]
    [InlineData("struct")]
    [InlineData("func main() { let a: array<")]
    [InlineData("}{")]
    [InlineData("func main() { x. }")]
    [InlineData("import")]
    public void GetDiagnostics_DoesNotThrowOnMalformedInput(string text)
    {
        var compilation = Compile(text);

        var diagnostics = compilation.GetDiagnostics();

        Assert.NotEmpty(diagnostics);
    }
}
