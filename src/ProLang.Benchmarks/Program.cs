using BenchmarkDotNet.Running;

namespace ProLang.Benchmarks;

internal class Program
{
    static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<CompilerBenchmarks>();
    }
}
