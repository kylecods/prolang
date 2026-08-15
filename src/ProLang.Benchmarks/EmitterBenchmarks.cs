using System.Text;
using BenchmarkDotNet.Attributes;
using ProLang.Compiler;
using ProLang.Intermediate;
using ProLang.Syntax;

namespace ProLang.Benchmarks;

/// <summary>
/// Benchmarks aimed specifically at the .NET backend's known hot paths.
/// </summary>
/// <remarks>
/// <para>
/// These exist to measure the code generation refactor rather than the pipeline as a whole.
/// The dominant cost in the pre-refactor emitter is metadata resolution:
/// <c>ResolveMethod</c> has no cache and linearly scans every type in every loaded reference
/// assembly — <c>System.Private.CoreLib</c> included — on each call, and it is called once per
/// emitted intrinsic call site rather than once per distinct member.
/// </para>
/// <para>
/// <see cref="IntrinsicCallSites"/> is therefore the headline number: it scales the count of
/// builtin call sites while holding everything else roughly constant, so a resolution cache
/// should flatten it dramatically. <see cref="ColdStart"/> captures the fixed cost of standing
/// an emitter up, which the reference-assembly probe dominates.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class EmitterBenchmarks
{
    private BoundProgram _intrinsicHeavy = null!;
    private BoundProgram _structHeavy = null!;
    private BoundProgram _minimal = null!;
    private MemoryStream _outputStream = null!;

    /// <summary>Number of builtin call sites in the intrinsic-heavy program.</summary>
    [Params(10, 100, 400)]
    public int CallSites { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _outputStream = new MemoryStream(capacity: 128 * 1024);
        _intrinsicHeavy = Bind(GenerateIntrinsicHeavy(CallSites));
        _structHeavy = Bind(GenerateStructHeavy(CallSites));
        _minimal = Bind("import \"io\"\n\nfunc main() { print(1) }");
    }

    [GlobalCleanup]
    public void Cleanup() => _outputStream.Dispose();

    /// <summary>
    /// Emission of a program dominated by builtin call sites — the metadata-resolution hot path.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Emit: intrinsic call sites")]
    public long IntrinsicCallSites() => EmitToStream(_intrinsicHeavy, "Intrinsics");

    /// <summary>
    /// Emission of a program dominated by struct definitions and field access — the
    /// type-resolution and struct-emission path.
    /// </summary>
    [Benchmark(Description = "Emit: struct definitions")]
    public long StructDefinitions() => EmitToStream(_structHeavy, "Structs");

    /// <summary>
    /// Emission of a trivial program. Everything above the noise floor here is fixed emitter
    /// setup cost: locating and reading the reference assemblies, and resolving well-known members.
    /// </summary>
    [Benchmark(Description = "Emit: cold start overhead")]
    public long ColdStart() => EmitToStream(_minimal, "Minimal");

    private long EmitToStream(BoundProgram program, string moduleName)
    {
        _outputStream.SetLength(0);
        Emitter.Emit(program, moduleName, [], _outputStream);

        return _outputStream.Length;
    }

    private static BoundProgram Bind(string source) => BenchmarkPrograms.Bind(source);

    /// <summary>
    /// Builds a program with <paramref name="callSites"/> builtin invocations, cycling through
    /// several distinct builtins so that per-member resolution and per-call-site resolution can
    /// be told apart: a cache keyed by member flattens this curve, a cache keyed by call site
    /// does not.
    /// </summary>
    private static string GenerateIntrinsicHeavy(int callSites)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import \"io\"");
        sb.AppendLine("import \"math\"");
        sb.AppendLine();
        sb.AppendLine("func main() {");
        sb.AppendLine("    let text: string = \"benchmark input string\"");
        sb.AppendLine("    let n: int = 0");

        for (var i = 0; i < callSites; i++)
        {
            switch (i % 5)
            {
                case 0: sb.AppendLine("    n = n + text.length()"); break;
                case 1: sb.AppendLine("    n = n + text.indexOf(\"m\")"); break;
                case 2: sb.AppendLine("    n = n + min(n, 7)"); break;
                case 3: sb.AppendLine("    n = n + max(n, 3)"); break;
                default: sb.AppendLine("    print(n)"); break;
            }
        }

        sb.AppendLine("    print(n)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a program with a struct definition and a field read per call site, exercising type
    /// resolution and the struct-name lookup table.
    /// </summary>
    private static string GenerateStructHeavy(int callSites)
    {
        var structCount = Math.Max(1, callSites / 4);
        var sb = new StringBuilder();

        sb.AppendLine("import \"io\"");
        sb.AppendLine();

        for (var i = 0; i < structCount; i++)
        {
            sb.AppendLine($"struct S{i} {{");
            sb.AppendLine("    a: int;");
            sb.AppendLine("    b: string;");
            sb.AppendLine("    c: bool;");
            sb.AppendLine("}");
        }

        sb.AppendLine("func main() {");
        sb.AppendLine("    let total: int = 0");

        for (var i = 0; i < structCount; i++)
        {
            sb.AppendLine($"    let s{i}: S{i} = S{i} {{ a: {i}, b: \"x\", c: true }}");
            sb.AppendLine($"    total = total + s{i}.a");
        }

        sb.AppendLine("    print(total)");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
