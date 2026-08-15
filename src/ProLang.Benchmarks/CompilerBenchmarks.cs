using BenchmarkDotNet.Attributes;
using ProLang.Compiler;
using ProLang.Intermediate;
using ProLang.Syntax;

namespace ProLang.Benchmarks;

/// <summary>
/// Measures each compiler phase in isolation, and the whole pipeline, across five workloads.
/// </summary>
/// <remarks>
/// <para>
/// Phases are genuinely isolated. That takes some care with this compiler:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Binding is lazy. <see cref="ProLangCompilation.Create"/> only resolves imports; the global
///     scope is produced on first access to an internal property, and the bound program on a call
///     to <see cref="ProLangCompilation.GetBoundProgram"/>. A benchmark that stops at
///     <c>Create</c> measures parsing, not binding.
///     </description>
///   </item>
///   <item>
///     <description>
///     Emission is measured from a pre-bound program prepared in <see cref="Setup"/>, so the
///     figure is code generation alone rather than the full pipeline under a different name.
///     </description>
///   </item>
///   <item>
///     <description>
///     Output goes to a <see cref="MemoryStream"/>. Writing a real <c>.dll</c> inside the measured
///     region puts filesystem latency and on-access virus scanning into the numbers.
///     </description>
///   </item>
/// </list>
/// <para>
/// Every benchmark returns its result so the JIT cannot eliminate the work as dead code.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CompilerBenchmarks
{
    private readonly Dictionary<string, BoundProgram> _boundPrograms = [];
    private readonly Dictionary<string, SyntaxTree> _syntaxTrees = [];
    private MemoryStream _outputStream = null!;

    /// <summary>The program under test. Set by BenchmarkDotNet from <see cref="AllWorkloads"/>.</summary>
    [ParamsSource(nameof(AllWorkloads))]
    public Workload Workload { get; set; } = null!;

    /// <summary>Feed for <see cref="Workload"/>.</summary>
    public static IEnumerable<Workload> AllWorkloads => Workloads.All;

    [GlobalSetup]
    public void Setup()
    {
        _outputStream = new MemoryStream(capacity: 64 * 1024);

        // Pre-compute the inputs each phase benchmark starts from, so that no benchmark pays for
        // an earlier phase it is not trying to measure. BenchmarkPrograms.Bind throws if a
        // workload does not compile, rather than letting Emit silently early-return.
        foreach (var workload in Workloads.All)
        {
            _syntaxTrees[workload.Name] = SyntaxTree.Parse(workload.Source);
            _boundPrograms[workload.Name] = BenchmarkPrograms.Bind(workload.Source);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _outputStream.Dispose();

    /// <summary>Lexing and parsing only.</summary>
    [Benchmark(Baseline = true, Description = "Parse")]
    public SyntaxTree Parse() => SyntaxTree.Parse(Workload.Source);

    /// <summary>
    /// Binding and lowering, starting from an already-parsed tree.
    /// </summary>
    /// <remarks>
    /// <c>GetBoundProgram</c> is what actually forces the global scope, symbol binding, and the
    /// lowering pass — creating the compilation alone does none of that.
    /// </remarks>
    [Benchmark(Description = "Bind + Lower")]
    public BoundProgram Bind() =>
        ProLangCompilation.Create(_syntaxTrees[Workload.Name]).GetBoundProgram();

    /// <summary>Code generation only, starting from an already-bound program.</summary>
    [Benchmark(Description = "Emit")]
    public long Emit()
    {
        _outputStream.SetLength(0);
        Emitter.Emit(_boundPrograms[Workload.Name], Workload.Name, [], _outputStream);

        return _outputStream.Length;
    }

    /// <summary>The whole pipeline, from source text to an assembly in memory.</summary>
    [Benchmark(Description = "Full pipeline")]
    public long FullPipeline()
    {
        _outputStream.SetLength(0);

        var syntaxTree = SyntaxTree.Parse(Workload.Source);
        var program = ProLangCompilation.Create(syntaxTree).GetBoundProgram();
        Emitter.Emit(program, Workload.Name, [], _outputStream);

        return _outputStream.Length;
    }
}
