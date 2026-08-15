using ProLang.Compiler;
using ProLang.Intermediate;
using ProLang.Syntax;

namespace ProLang.Benchmarks;

/// <summary>
/// Binds benchmark source text, refusing to hand back a program that failed to compile.
/// </summary>
/// <remarks>
/// <para>
/// This guard exists because of a real failure: the original benchmark workloads were written
/// with <c>var x = 5</c>, but ProLang's binding keyword is <c>let</c>. Every workload therefore
/// failed to bind, and <c>Emit</c> — which returns early when the program carries diagnostics —
/// did no work at all. The suite reported single-digit nanosecond timings with zero allocations
/// and nobody noticed, because a benchmark that measures nothing still produces a number.
/// </para>
/// <para>
/// Failing loudly in <c>[GlobalSetup]</c> is the fix: a workload that stops compiling now aborts
/// the run instead of quietly reporting an unbeatable score.
/// </para>
/// </remarks>
internal static class BenchmarkPrograms
{
    /// <summary>
    /// Parses and binds <paramref name="source"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The source failed to parse or bind. The message carries the diagnostics.
    /// </exception>
    public static BoundProgram Bind(string source)
    {
        var syntaxTree = SyntaxTree.Parse(source);

        if (syntaxTree.Diagnostics.Any())
        {
            throw new InvalidOperationException(
                "Benchmark workload failed to parse:" + Environment.NewLine +
                string.Join(Environment.NewLine, syntaxTree.Diagnostics.Select(d => "  " + d.Message)) +
                Environment.NewLine + Environment.NewLine + source);
        }

        var program = ProLangCompilation.Create(syntaxTree).GetBoundProgram();

        if (program.Diagnostics.Any())
        {
            throw new InvalidOperationException(
                "Benchmark workload failed to bind, so emission would measure nothing:" + Environment.NewLine +
                string.Join(Environment.NewLine, program.Diagnostics.Select(d => "  " + d.Message)) +
                Environment.NewLine + Environment.NewLine + source);
        }

        return program;
    }
}
