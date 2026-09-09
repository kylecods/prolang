using System.Collections.Immutable;
using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Tests.Compiler;

/// <summary>
/// Declaring a struct's name before binding its field types, and the one shape that stays an error.
/// </summary>
/// <remarks>
/// The binder used to bind field types before declaring the struct, in a single sequential pass over
/// the declarations, so a field could only name a struct declared textually earlier. Both emitters
/// already tolerated the general case — <c>TypeEmitter</c> registers a definition before adding its
/// fields, <c>CEmitter</c> emits forward typedefs — so the restriction was the binder's alone.
/// </remarks>
public class StructDeclarationBindingTests
{
    private static ProLangCompilation Compile(string text) =>
        ProLangCompilation.Create(SyntaxTree.Parse(SourceText.From(text, "test.prl")));

    [Fact]
    public void AStructField_CanNameAStructDeclaredLater()
    {
        var compilation = Compile("""
            struct Outer {
                inner: Inner;
            }

            struct Inner {
                value: int;
            }

            func main() {
                let o = Outer { inner: Inner { value: 1 } }
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void AStructField_CanNameItsOwnStructThroughAnArray()
    {
        var compilation = Compile("""
            func main() {
                let n = Node { value: 1, kids: array_new(0) }
            }

            struct Node {
                value: int;
                kids:  array<Node>;
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    [Fact]
    public void TwoStructs_CanReferToEachOtherThroughArrays()
    {
        var compilation = Compile("""
            struct Ping {
                pongs: array<Pong>;
            }

            struct Pong {
                pings: array<Ping>;
            }

            func main() {
                let p = Ping { pongs: array_new(0) }
            }
            """);

        Assert.Empty(compilation.GetDiagnostics());
    }

    /// <summary>
    /// A struct holding itself by value has no finite size, and nothing downstream would catch it.
    /// </summary>
    /// <remarks>
    /// <c>TypeEmitter</c> would emit a value type whose field is that same value type, and the CLR
    /// would reject it at load time with a <c>TypeLoadException</c> pointing at nothing in the source.
    /// The old field-before-declaration ordering made this unreachable by reporting an undefined type
    /// instead, so removing that ordering means the real check has to exist.
    /// </remarks>
    [Fact]
    public void AStructContainingItselfByValue_IsReported()
    {
        var compilation = Compile("""
            struct SelfDirect {
                next: SelfDirect;
            }

            func main() { }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("SelfDirect", diagnostic.Message);
        Assert.Contains("no finite size", diagnostic.Message);
    }

    [Fact]
    public void StructsContainingEachOtherByValue_AreReported()
    {
        var compilation = Compile("""
            struct A {
                b: B;
            }

            struct B {
                a: A;
            }

            func main() { }
            """);

        var diagnostic = Assert.Single(compilation.GetDiagnostics());

        Assert.Contains("no finite size", diagnostic.Message);
    }

    /// <summary>
    /// Reading a declared struct's fields before the second pass completes them throws.
    /// </summary>
    /// <remarks>
    /// Returning an empty field list instead would be invisible: the caller emits a struct with no
    /// fields, <c>TypeEmitter</c> caches that definition under the struct's name, and the assembly
    /// verifies, runs, and produces wrong answers.
    /// </remarks>
    [Fact]
    public void ReadingFieldsBeforeTheyAreCompleted_Throws()
    {
        var declared = StructSymbol.Declaring("Pending", ImmutableArray<TypeParameterSymbol>.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() => declared.Fields);

        Assert.Contains("Pending", exception.Message);
    }

    [Fact]
    public void CompletingFieldsTwice_Throws()
    {
        var declared = StructSymbol.Declaring("Pending", ImmutableArray<TypeParameterSymbol>.Empty);
        declared.CompleteFields(ImmutableArray<StructField>.Empty);

        Assert.Throws<InvalidOperationException>(() => declared.CompleteFields(ImmutableArray<StructField>.Empty));
    }

    /// <summary>
    /// A generic instantiated while its own template is still unbound still gets the template's fields.
    /// </summary>
    /// <remarks>
    /// <c>BindTypeSyntax</c> instantiates during field binding, so <c>struct Holder { b: Box&lt;int&gt; }</c>
    /// declared ahead of <c>Box</c> reaches <c>InstantiateGeneric</c> before <c>Box</c> has any fields.
    /// Substituting eagerly captured the empty list; the substitution is deferred to first read
    /// instead. This is the failure that would have been silent — a zero-field type, cached by name.
    /// </remarks>
    [Fact]
    public void AGenericInstantiatedBeforeItsTemplateIsBound_StillGetsItsFields()
    {
        var typeParameter = new TypeParameterSymbol("T", 0);
        var template = StructSymbol.Declaring("Box", ImmutableArray.Create(typeParameter));

        // Instantiated first, exactly as the binder does when a field names Box<int> ahead of Box.
        var instantiated = template.InstantiateGeneric(TypeSymbol.Int);

        template.CompleteFields(ImmutableArray.Create(
            new StructField("value", typeParameter),
            new StructField("count", TypeSymbol.Int)));

        Assert.Collection(
            instantiated.Fields,
            field =>
            {
                Assert.Equal("value", field.Name);
                Assert.Equal(TypeSymbol.Int, field.Type);
            },
            field =>
            {
                Assert.Equal("count", field.Name);
                Assert.Equal(TypeSymbol.Int, field.Type);
            });
    }
}
