using ProLang.Compiler;
using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Asserts that the IL emitted for each corpus program is unchanged.
/// </summary>
/// <remarks>
/// This is the primary regression gate for the code generation refactor. Every mechanical
/// extraction step is required to leave these snapshots byte-identical; only the two checkpoints
/// that intentionally change codegen (the temp-local pool, and moving builtins into
/// ProLang.Runtime) may re-baseline them.
/// <para>
/// The listing comes from <see cref="MsilDisassembler"/>, which already existed to serve the
/// <c>--msil</c> CLI flag and writes to an injectable <see cref="TextWriter"/>.
/// </para>
/// </remarks>
public sealed class IlSnapshotTests
{
    [Theory]
    [MemberData(nameof(Corpus))]
    public void EmittedIl_MatchesSnapshot(string relativePath)
    {
        var entry = TestCorpus.Get(relativePath);

        using var scratch = ScratchDirectory.Create(entry.Name);
        var result = CompilerHarness.CompileToFile(scratch.Path, entry.FullPath);

        Assert.True(
            result.Succeeded,
            $"Expected '{entry.RelativePath}' to compile, but got diagnostics:{Environment.NewLine}{result.DiagnosticText}");

        using var writer = new StringWriter();
        MsilDisassembler.Disassemble(result.AssemblyPath!, writer);

        Snapshot.Verify(TestPaths.SnapshotsDirectory, entry.Name + ".il.txt", writer.ToString());
    }

    public static TheoryData<string> Corpus => TestCorpus.CompilableData;
}
