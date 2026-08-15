using System.Reflection;
using BenchmarkDotNet.Running;

namespace ProLang.Benchmarks;

/// <summary>
/// Entry point for the compiler benchmark suite.
/// </summary>
/// <remarks>
/// Uses <see cref="BenchmarkSwitcher"/> rather than <c>BenchmarkRunner.Run&lt;T&gt;()</c> so that
/// command-line arguments are honoured. With the runner, a hardcoded benchmark class silently
/// ignores <c>--filter</c>, <c>--job</c>, and every other documented switch.
/// <para>Examples:</para>
/// <code>
/// dotnet run -c Release --project src/ProLang.Benchmarks -- --filter "*"
/// dotnet run -c Release --project src/ProLang.Benchmarks -- --filter "*EmitterBenchmarks*"
/// dotnet run -c Release --project src/ProLang.Benchmarks -- --job short --filter "*Emit*"
/// </code>
/// </remarks>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}
