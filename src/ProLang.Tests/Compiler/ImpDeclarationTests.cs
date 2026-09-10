using ProLang.Compiler;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Tests.Compiler;

/// <summary>
/// <c>imp</c> blocks: what binds, and what is refused.
/// </summary>
/// <remarks>
/// A function is identified by its name alone — the scope's function table, the emitter's method map
/// and both bound-program dictionaries all key off it — so an imp member is declared under a
/// qualified name. That name is an implementation detail and must never surface in a message, which
/// several of these tests assert directly.
/// </remarks>
public class ImpDeclarationTests
{
    private static ProLangCompilation Compile(string text) =>
        ProLangCompilation.Create(SyntaxTree.Parse(SourceText.From(text, "test.prl")));

    private const string Rect = """
        struct Rect {
            x: int;
            y: int;
        }

        imp Rect {
            func new(x: int, y: int): Rect { return Rect { x: x, y: y } }
            func area(self: Rect): int { return self.x * self.y }
        }
        """;

    [Fact]
    public void AnImpBlock_BindsAssociatedAndInstanceFunctions()
    {
        var compilation = Compile($$"""
            {{Rect}}

            func main() {
                let r = Rect->new(2, 3)
                let a = r->area()
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// Two types may each declare a member of the same name.
    /// </summary>
    /// <remarks>
    /// This is the whole reason qualification exists. The scope's function table is keyed by a bare
    /// string, so without it the second <c>new</c> would collide with the first.
    /// </remarks>
    [Fact]
    public void TwoTypes_CanEachDeclareAMemberOfTheSameName()
    {
        var compilation = Compile("""
            struct A { v: int; }
            struct B { v: int; }

            imp A { func new(): A { return A { v: 1 } } }
            imp B { func new(): B { return B { v: 2 } } }

            func main() {
                let a = A->new()
                let b = B->new()
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void SeveralImpBlocks_MayTargetOneType()
    {
        var compilation = Compile("""
            struct Point { x: int; }

            imp Point { func new(): Point { return Point { x: 0 } } }
            imp Point { func get(self: Point): int { return self.x } }

            func main() {
                let p = Point->new()
                let v = p->get()
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void AnImpMemberDeclaredTwice_IsReported()
    {
        var compilation = Compile("""
            struct Point { x: int; }

            imp Point {
                func get(self: Point): int { return self.x }
                func get(self: Point): int { return 0 }
            }

            func main() { }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("Point->get", diagnostic.Message);
        Assert.DoesNotContain("Point__get", diagnostic.Message);
    }

    [Fact]
    public void AnImpBlockOnSomethingThatIsNotAType_IsReported()
    {
        var compilation = Compile("""
            imp Nope { func f(): int { return 0 } }

            func main() { }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());
        Assert.Contains("Nope", diagnostic.Message);
    }

    /// <summary>
    /// <c>self</c> must come first, because the receiver is passed as argument 0.
    /// </summary>
    [Fact]
    public void ASelfParameterThatIsNotFirst_IsReported()
    {
        var compilation = Compile("""
            struct Point { x: int; }

            imp Point {
                func f(n: int, self: Point): int { return n }
            }

            func main() { }
            """);

        Assert.Contains(compilation.GetDiagnostics(), d => d.Message.Contains("first parameter"));
    }

    [Fact]
    public void ASelfParameterOfTheWrongType_IsReported()
    {
        var compilation = Compile("""
            struct Point { x: int; }

            imp Point {
                func f(self: int): int { return self }
            }

            func main() { }
            """);

        Assert.Contains(compilation.GetDiagnostics(), d => d.Message.Contains("must have type 'Point'"));
    }

    [Fact]
    public void ASelfParameterOnATopLevelFunction_IsReported()
    {
        var compilation = Compile("""
            func f(self: int): int { return self }

            func main() { }
            """);

        Assert.Contains(compilation.GetDiagnostics(), d => d.Message.Contains("only means anything inside an imp block"));
    }

    [Fact]
    public void AnInstanceMethodReachedOnTheType_IsReportedWithTheFix()
    {
        var compilation = Compile($$"""
            {{Rect}}

            func main() {
                let r = Rect->new(1, 2)
                let a = Rect->area()
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("value->area(...)", diagnostic.Message);
    }

    [Fact]
    public void AnAssociatedFunctionReachedOnAValue_IsReportedWithTheFix()
    {
        var compilation = Compile($$"""
            {{Rect}}

            func main() {
                let r = Rect->new(1, 2)
                let b = r->new(3, 4)
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("Rect->new(...)", diagnostic.Message);
    }

    [Fact]
    public void AnUnknownMember_IsReported()
    {
        var compilation = Compile($$"""
            {{Rect}}

            func main() {
                let r = Rect->new(1, 2)
                let a = r->missing()
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("'Rect' has no function named 'missing'", diagnostic.Message);
    }

    /// <summary>
    /// A function value cannot carry a receiver.
    /// </summary>
    /// <remarks>
    /// A ProLang function value is emitted as a bare pointer with a null delegate target, so there is
    /// nowhere to keep one. The message points at <c>Type-&gt;member</c>, which is a value.
    /// </remarks>
    [Fact]
    public void AMethodReferenceBoundToAValue_IsReported()
    {
        var compilation = Compile($$"""
            {{Rect}}

            func main() {
                let r = Rect->new(1, 2)
                let f = r->area
            }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("cannot capture a receiver", diagnostic.Message);
        Assert.Contains("Rect->area", diagnostic.Message);
    }

    /// <summary>
    /// A generic type is refused for now, rather than silently mis-inferring.
    /// </summary>
    /// <remarks>
    /// Type arguments cannot be inferred through a struct instantiation — <c>ExtractTypeArgs</c>
    /// recurses on <c>TypeArguments</c>, which a monomorphised struct leaves empty — so
    /// <c>arr-&gt;get(0)</c> would infer <c>any</c> and fail at conversion with a confusing message.
    /// </remarks>
    [Fact]
    public void AnImpBlockOnAGenericType_IsReported()
    {
        var compilation = Compile("""
            struct Box<T> { value: T; }

            imp Box {
                func get(self: Box<int>): int { return self.value }
            }

            func main() { }
            """);

        Assert.Contains(compilation.GetDiagnostics(), d => d.Message.Contains("generic type"));
    }

    [Fact]
    public void ATopLevelFunction_StillBindsWithoutAnImpBlock()
    {
        var compilation = Compile("""
            struct Point { x: int; }

            func point_get(p: Point): int { return p.x }

            func main() {
                let p = Point { x: 1 }
                let v = point_get(p)
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }
}
