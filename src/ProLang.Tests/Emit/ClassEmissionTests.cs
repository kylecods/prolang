using System.Collections.Immutable;
using Mono.Cecil;
using ProLang.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Tests.Infrastructure;
using ProLang.Text;

namespace ProLang.Tests.Emit;

/// <summary>
/// The metadata shape of an emitted <c>class</c>, and the value type it must not disturb.
/// </summary>
/// <remarks>
/// Value-versus-reference is decided in one place — the base type and attributes
/// <c>TypeEmitter.EmitStruct</c> passes to Cecil — and everything downstream reads it back off the
/// Cecil flag rather than off a ProLang symbol. So these assertions cover more than they look like:
/// get the base type wrong and boxing, default values, field assignment and casts all follow it.
/// </remarks>
public sealed class ClassEmissionTests
{
    private const string ClassProgram = "tests/language/classes/reference-semantics.prl";

    private static TypeDefinition FindType(AssemblyDefinition assembly, string name) =>
        assembly.Modules
            .SelectMany(m => m.Types)
            .FirstOrDefault(t => t.Name == name)
        ?? throw new InvalidOperationException($"No type '{name}' in the emitted assembly.");

    private static AssemblyDefinition CompileCorpusProgram(ScratchDirectory scratch)
    {
        var entry = TestCorpus.Get(ClassProgram);
        var result = CompilerHarness.CompileToFile(scratch.Path, entry.FullPath);

        Assert.True(
            result.Succeeded,
            $"Expected '{entry.RelativePath}' to compile, but got:{Environment.NewLine}{result.DiagnosticText}");

        return AssemblyDefinition.ReadAssembly(result.AssemblyPath!, new ReaderParameters { ReadSymbols = false });
    }

    [Fact]
    public void AClass_IsEmittedAsASealedReferenceTypeWithAConstructor()
    {
        using var scratch = ScratchDirectory.Create("class-shape");
        using var assembly = CompileCorpusProgram(scratch);

        var counter = FindType(assembly, "Counter");

        Assert.False(counter.IsValueType);
        Assert.Equal("System.Object", counter.BaseType.FullName);
        Assert.True(counter.IsSealed, "ProLang has no inheritance, so a class stays sealed.");

        // SequentialLayout pins field order and blocks the runtime from packing reference fields.
        Assert.False(counter.IsSequentialLayout);

        // newobj needs a constructor reference, and initobj on a class only nulls a slot — there is
        // no way to allocate one without this.
        var constructor = Assert.Single(counter.Methods.Where(m => m.IsConstructor));
        Assert.Empty(constructor.Parameters);
        Assert.True(constructor.HasBody);
    }

    [Fact]
    public void AStruct_IsStillEmittedAsASequentialValueType()
    {
        using var scratch = ScratchDirectory.Create("struct-shape");
        using var assembly = CompileCorpusProgram(scratch);

        var valueCounter = FindType(assembly, "ValueCounter");

        Assert.True(valueCounter.IsValueType);
        Assert.Equal("System.ValueType", valueCounter.BaseType.FullName);
        Assert.True(valueCounter.IsSequentialLayout);
        Assert.Empty(valueCounter.Methods.Where(m => m.IsConstructor));
    }

    [Fact]
    public void AClassField_HoldsAReferenceRatherThanAnEmbeddedValue()
    {
        using var scratch = ScratchDirectory.Create("class-field");
        using var assembly = CompileCorpusProgram(scratch);

        var pair = FindType(assembly, "Pair");
        var left = Assert.Single(pair.Fields.Where(f => f.Name == "left"));

        Assert.Equal("Counter", left.FieldType.Name);
        Assert.False(left.FieldType.IsValueType);
    }

    /// <summary>
    /// A generic class stays a class once monomorphised.
    /// </summary>
    /// <remarks>
    /// The binder substitutes type arguments by building a fresh symbol, and dropping the flag there
    /// would emit the instantiation as a value type while its template is a class. Nothing would
    /// catch it: struct definitions are cached by name, so the two are never compared.
    /// </remarks>
    [Fact]
    public void InstantiatingAGenericClass_KeepsItAReferenceType()
    {
        var typeParameter = new TypeParameterSymbol("T", 0);
        var template = StructSymbol.Declaring("Box", ImmutableArray.Create(typeParameter), isReferenceType: true);
        template.CompleteFields(ImmutableArray.Create(new StructField("value", typeParameter)));

        var instantiated = template.InstantiateGeneric(TypeSymbol.Int);

        Assert.True(
            instantiated.IsReferenceType,
            "Box<int> must stay a reference type, or it is emitted as a value type under a name "
            + "the emitter caches and never revisits.");
    }

    [Fact]
    public void InstantiatingAGenericStruct_KeepsItAValueType()
    {
        var typeParameter = new TypeParameterSymbol("T", 0);
        var template = StructSymbol.Declaring("Holder", ImmutableArray.Create(typeParameter));
        template.CompleteFields(ImmutableArray.Create(new StructField("value", typeParameter)));

        Assert.False(template.InstantiateGeneric(TypeSymbol.Int).IsReferenceType);
    }

    /// <summary>
    /// A generic struct is reported by the C backend rather than dropped.
    /// </summary>
    /// <remarks>
    /// The emission loops skip generic structs, so the generated C referred to a type it never
    /// declared and the failure landed as a C compiler or linker error against machine-written code
    /// with no line to point at. Classes used to be in the same position; they are not any more, and
    /// <see cref="CBackendTests"/> compiles and runs one to prove it.
    /// </remarks>
    [Fact]
    public void TheCBackend_ReportsAGenericStructRatherThanSkippingIt()
    {
        using var scratch = ScratchDirectory.Create("generic-emit-c");

        var compilation = ProLangCompilation.Create(SyntaxTree.Parse(SourceText.From("""
            struct Box<T> {
                value: T;
            }

            func main() {
                let b = Box<int> { value: 1 }
            }
            """, "test.prl")));

        var diagnostics = compilation.EmitC("test", scratch.Path);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("Box", diagnostic.Message);
    }
}
