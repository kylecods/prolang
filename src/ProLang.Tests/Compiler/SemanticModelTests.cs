using ProLang.Symbols;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Compiler;

/// <summary>
/// The position-to-symbol layer every editor navigation feature is built on.
/// </summary>
/// <remarks>
/// The bound tree cannot answer any of these questions — its nodes carry no spans, and a type
/// clause never becomes a bound node at all — so the binder records what it resolves as it goes.
/// These tests are what say that recording is complete enough to navigate by.
/// </remarks>
public class SemanticModelTests
{
    [Fact]
    public void SymbolAt_FindsALocalVariable()
    {
        var source = MarkedSource.Parse("""
            import "io"

            func main() {
                let count: int = 1
                print(cou|nt)
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.NotNull(symbol);
        Assert.Equal("count", symbol.Name);
        Assert.Equal(SymbolKind.LocalVariable, symbol.Kind);
    }

    [Fact]
    public void SymbolAt_FindsAParameter()
    {
        var source = MarkedSource.Parse("""
            func double_it(value: int) : int {
                return val|ue * 2
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal(SymbolKind.Parameter, symbol?.Kind);
    }

    [Fact]
    public void SymbolAt_FindsAFunctionAtItsCallSite()
    {
        var source = MarkedSource.Parse("""
            func helper() : int { return 1 }

            func main() {
                let x: int = hel|per()
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("helper", symbol?.Name);
        Assert.Equal(SymbolKind.Function, symbol?.Kind);
    }

    /// <summary>
    /// The navigation that no amount of annotating the bound tree could have provided, because
    /// binding a type clause produces a bare type symbol and never a bound node.
    /// </summary>
    [Fact]
    public void SymbolAt_FindsAStructNamedInATypeClause()
    {
        var source = MarkedSource.Parse("""
            struct Point { x: int; y: int }

            func main() {
                let p: Po|int = Point { x: 1, y: 2 }
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("Point", symbol?.Name);
        Assert.Equal(SymbolKind.Struct, symbol?.Kind);
    }

    [Fact]
    public void SymbolAt_FindsAStructField()
    {
        var source = MarkedSource.Parse("""
            import "io"

            struct Point { x: int; y: int }

            func main() {
                let p: Point = Point { x: 1, y: 2 }
                print(p.|x)
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("x", symbol?.Name);
        Assert.Equal(SymbolKind.Field, symbol?.Kind);
    }

    [Fact]
    public void SymbolAt_FindsAnEnumMember()
    {
        var source = MarkedSource.Parse("""
            import "io"

            enum Colour { Red, Green = 5 }

            func main() {
                print(Colour.Gr|een)
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("Green", symbol?.Name);
        Assert.Equal(SymbolKind.EnumMember, symbol?.Kind);
        Assert.Equal(5, Assert.IsType<EnumMemberSymbol>(symbol).Value);
    }

    [Fact]
    public void FindDefinition_PointsAtTheDeclaringIdentifier()
    {
        var source = MarkedSource.Parse("""
            func helper() : int { return 1 }

            func main() {
                let x: int = hel|per()
            }
            """);

        var model = source.Bind();
        var symbol = model.SymbolAt(source.FileName, source.Position);

        var definition = model.FindDefinition(symbol!);

        Assert.NotNull(definition);
        Assert.Equal("helper", source.TextOf(definition.Value.Span));
        Assert.Equal(0, definition.Value.StartLine);
    }

    [Fact]
    public void FindReferences_FindsTheDeclarationAndEveryUse()
    {
        var source = MarkedSource.Parse("""
            import "io"

            func main() {
                let tot|al: int = 0
                total = total + 1
                print(total)
            }
            """);

        var model = source.Bind();
        var symbol = model.SymbolAt(source.FileName, source.Position);

        var references = model.FindReferences(symbol!);

        Assert.Equal(4, references.Length);
        Assert.All(references, r => Assert.Equal("total", source.TextOf(r.Span)));
    }

    /// <summary>
    /// Two locals of the same name in different functions are different symbols.
    /// </summary>
    /// <remarks>
    /// The distinction a text search cannot make, and the reason the old regex server's rename was
    /// unsafe. It rests on symbol identity being reference equality for variables.
    /// </remarks>
    [Fact]
    public void FindReferences_DoesNotConfuseSameNamedLocalsInDifferentFunctions()
    {
        var source = MarkedSource.Parse("""
            import "io"

            func first() {
                let val|ue: int = 1
                print(value)
            }

            func second() {
                let value: int = 2
                print(value)
            }
            """);

        var model = source.Bind();
        var symbol = model.SymbolAt(source.FileName, source.Position);

        var references = model.FindReferences(symbol!);

        // The declaration and the one use inside first(), and nothing from second().
        Assert.Equal(2, references.Length);
        Assert.Equal([3, 4], references.Select(r => r.StartLine).Order());
    }

    [Fact]
    public void TypeOfExpression_TypesACallResult()
    {
        var source = MarkedSource.Parse("""
            struct Point { x: int; y: int }

            func origin() : Point { return Point { x: 0, y: 0 } }

            func main() {
                let p: Point = |origin()|
            }
            """);

        var model = source.Bind();
        var span = ProLang.Text.TextSpan.FromBounds(source.Positions[0], source.Positions[1]);

        var type = model.TypeOfExpression(source.FileName, span);

        Assert.Equal("Point", type?.Name);
    }

    /// <summary>
    /// A generic function's body is navigable even though code generation never binds a template.
    /// </summary>
    [Fact]
    public void SymbolAt_FindsNamesInsideAnUncalledGenericFunction()
    {
        var source = MarkedSource.Parse("""
            func identity<T>(value: T) : T {
                return val|ue
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("value", symbol?.Name);
        Assert.Equal(SymbolKind.Parameter, symbol?.Kind);
    }

    /// <summary>
    /// Go-to-definition on a standard library function lands in the library's own source.
    /// </summary>
    /// <remarks>
    /// The whole import graph binds into one compilation, so this needs nothing beyond what the
    /// recorder already collects: the call in this file and the declaration in <c>std/util.prl</c>
    /// are two occurrences of one symbol. The regex server could not do this at all — it refused
    /// to resolve any import whose path did not end in <c>.prl</c>, which is every std module.
    /// </remarks>
    [Fact]
    public void FindDefinition_CrossesIntoTheStandardLibrary()
    {
        var source = MarkedSource.Parse("""
            import "util"
            import "io"

            func main() {
                print(util_|abs(0 - 5))
            }
            """);

        var model = source.Bind();
        var symbol = model.SymbolAt(source.FileName, source.Position);

        Assert.Equal("util_abs", symbol?.Name);

        var definition = model.FindDefinition(symbol!);

        Assert.NotNull(definition);
        Assert.EndsWith("util.prl", definition.Value.FileName);
        Assert.Equal("util_abs", definition.Value.Text.ToString(definition.Value.Span));
    }

    [Fact]
    public void LookupSymbols_OffersLocalsInScopeAndDeclaredFunctions()
    {
        var source = MarkedSource.Parse("""
            import "io"

            func helper() : int { return 1 }

            func main() {
                let alpha: int = 1
                let beta: int = 2
                |
            }
            """);

        var names = source.Bind()
            .LookupSymbols(source.FileName, source.Position)
            .Select(s => s.Name)
            .ToList();

        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
        Assert.Contains("helper", names);
        Assert.Contains("print", names);
    }

    /// <summary>
    /// A local is not offered before the line that declares it.
    /// </summary>
    [Fact]
    public void LookupSymbols_DoesNotOfferALocalDeclaredLater()
    {
        var source = MarkedSource.Parse("""
            func main() {
                let alpha: int = 1
                |
                let omega: int = 2
            }
            """);

        var names = source.Bind()
            .LookupSymbols(source.FileName, source.Position)
            .Select(s => s.Name)
            .ToList();

        Assert.Contains("alpha", names);
        Assert.DoesNotContain("omega", names);
    }

    /// <summary>
    /// A local in one function is not offered inside another.
    /// </summary>
    [Fact]
    public void LookupSymbols_DoesNotLeakLocalsBetweenFunctions()
    {
        var source = MarkedSource.Parse("""
            func first() {
                let hidden: int = 1
            }

            func second() {
                |
            }
            """);

        var names = source.Bind()
            .LookupSymbols(source.FileName, source.Position)
            .Select(s => s.Name)
            .ToList();

        Assert.DoesNotContain("hidden", names);
    }
}
