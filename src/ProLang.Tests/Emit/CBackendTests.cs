using ProLang.Compiler;
using ProLang.Syntax;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Compiles the C backend's output with a real C compiler and runs it.
/// </summary>
/// <remarks>
/// <para>
/// Two assertions, and the second is the one that matters. That the generated C <em>compiles</em>
/// was never checked anywhere before — CI transpiled two programs and looked only for a crash. That
/// it produces the <em>same answers as the .NET backend</em> is what makes the C target a real
/// second implementation rather than a plausible-looking text file.
/// </para>
/// <para>
/// The expected output is the same <c>Expected/*.out</c> golden the .NET backend already asserts
/// against, so the two backends are held to one specification rather than to two.
/// </para>
/// <para>
/// The program list is a floor, not a ceiling: it covers the reference-type work plus a spread of
/// ordinary language features. Programs reaching .NET interop or the generic structs the C backend
/// does not emit are deliberately absent.
/// </para>
/// </remarks>
public sealed class CBackendTests
{
    public static TheoryData<string> CCompatiblePrograms =>
    [
        "tests/language/char-literals.prl",
        "tests/language/imp/imp-blocks.prl",
        "tests/language/classes/recursive.prl",
        "tests/language/classes/reference-semantics.prl",
        // forward-and-recursive-references.prl is deliberately absent: it declares a generic struct,
        // which this backend does not emit and now says so rather than dropping it silently.
        "tests/language/structs/array-of-structs.prl",
        "tests/language/structs/nested-field-assignment.prl",
        "tests/language/entry-point/basic-main.prl",
        "tests/language/entry-point/nested-calls.prl",
        "tests/language/entry-point/print-expressions.prl",
    ];

    [Theory]
    [MemberData(nameof(CCompatiblePrograms))]
    public void GeneratedC_CompilesAndMatchesTheDotNetBackend(string relativePath)
    {
        // A machine with no C compiler still runs the suite green; there is no skip primitive in
        // this project's xunit setup, so this returns rather than failing.
        if (!CToolchain.IsAvailable)
        {
            return;
        }

        var entry = TestCorpus.Get(relativePath);

        using var scratch = ScratchDirectory.Create("c-backend");

        var compilation = ProLangCompilation.Create(SyntaxTree.Load(entry.FullPath));
        var moduleName = Path.GetFileNameWithoutExtension(entry.FullPath);

        var diagnostics = compilation.EmitC(moduleName, scratch.Path);

        Assert.True(
            diagnostics.IsEmpty,
            $"'{entry.RelativePath}' did not transpile to C:{Environment.NewLine}"
            + string.Join(Environment.NewLine, diagnostics.Select(d => "  " + d.Message)));

        var sourceFile = Path.Combine(scratch.Path, moduleName + ".c");
        Assert.True(File.Exists(sourceFile), $"No C file was written for '{entry.RelativePath}'.");

        var executable = CToolchain.TryCompile(sourceFile, out var compilerOutput);

        Assert.True(
            executable != null,
            $"The C emitted for '{entry.RelativePath}' did not compile with {CToolchain.Description}:"
            + Environment.NewLine + compilerOutput);

        var (exitCode, actual) = CToolchain.Run(executable!);

        Assert.True(exitCode == 0, $"'{entry.RelativePath}' exited with {exitCode}:{Environment.NewLine}{actual}");

        var expected = File.ReadAllText(Path.Combine(TestPaths.ExpectedOutputDirectory, entry.Name + ".out"));

        Assert.Equal(Normalize(expected), Normalize(actual));
    }

    /// <summary>
    /// The C backend allocates classes from the arena and never frees them.
    /// </summary>
    /// <remarks>
    /// This is the honest contract, and it differs from .NET, where the same program is collected.
    /// The test pins the mechanism rather than the consequence: allocation goes through
    /// <c>prl_alloc_zero</c>, so an unwritten field starts at its default exactly as <c>newobj</c>
    /// guarantees on .NET, and there is no <c>free</c> anywhere in the output.
    /// </remarks>
    [Fact]
    public void AClass_IsAllocatedFromTheArenaAndNeverFreed()
    {
        using var scratch = ScratchDirectory.Create("c-arena");

        var source = Path.Combine(scratch.Path, "cls.prl");
        File.WriteAllText(source, """
            class Node {
                value: int;
                next:  Node;
            }

            func main() {
                let n = Node { value: 1, next: null }
            }
            """);

        var compilation = ProLangCompilation.Create(SyntaxTree.Load(source));
        Assert.Empty(compilation.EmitC("cls", scratch.Path));

        var generated = File.ReadAllText(Path.Combine(scratch.Path, "cls.c"));

        Assert.Contains("static Node* prl_new_Node(", generated);
        Assert.Contains("prl_alloc_zero(sizeof(Node))", generated);
        Assert.Contains("->", generated);
        Assert.DoesNotContain("free(", generated);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").TrimEnd();
}
