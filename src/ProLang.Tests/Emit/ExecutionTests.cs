using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Compiles each runnable corpus program, executes it, and asserts its stdout.
/// </summary>
/// <remarks>
/// This is what the previous script-based harness never did — it only grepped compiler stdout for
/// four error patterns and reported PASS for programs that crashed. Behavioural coverage matters
/// most at the one checkpoint where IL snapshots are expected to change wholesale (moving builtins
/// into ProLang.Runtime); there, these assertions are the only proof that behaviour held.
/// </remarks>
public sealed class ExecutionTests
{
    [Theory]
    [MemberData(nameof(Corpus))]
    public void CompiledProgram_ProducesExpectedOutput(string relativePath)
    {
        var entry = TestCorpus.Get(relativePath);

        using var scratch = ScratchDirectory.Create(entry.Name);
        var compileResult = CompilerHarness.CompileToFile(scratch.Path, entry.FullPath);

        Assert.True(
            compileResult.Succeeded,
            $"Expected '{entry.RelativePath}' to compile, but got diagnostics:{Environment.NewLine}{compileResult.DiagnosticText}");

        var runResult = ProgramRunner.Run(compileResult.AssemblyPath!);

        Assert.False(runResult.TimedOut, $"'{entry.RelativePath}' did not terminate within the timeout.");
        Assert.True(
            runResult.Succeeded,
            $"'{entry.RelativePath}' failed at runtime: {runResult.Describe()}");

        Snapshot.Verify(TestPaths.ExpectedOutputDirectory, entry.Name + ".out", runResult.StandardOutput);
    }

    public static TheoryData<string> Corpus => TestCorpus.RunnableData;
}
