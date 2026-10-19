using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ProLang.Mips;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { Console.WriteLine("ProLang.Mips run|profile program.dll [--engine=il|mips|optimized] [--frames=60] [--output=DIR] [--v-log=PATH] [--samples=5] [-- args]\nProLang.Mips self-test"); return 0; }
            if (args[0] == "self-test") { SelfTests.Run(); return 0; }
            if (args.Length < 2 || args[0] is not ("run" or "profile")) throw new ArgumentException("expected run|profile and an assembly path");
            string Option(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith("--" + name + "=", StringComparison.Ordinal))?[ (name.Length + 3)..] ?? fallback;
            int frames = int.Parse(Option("frames", "60"), CultureInfo.InvariantCulture);
            if (frames is < 2 or > 10000) throw new ArgumentException("frames must be between 2 and 10000");
            string output = Path.GetFullPath(Option("output", "artifacts/mips-cs"));
            string[] programArgs = args.Contains("--") ? args[(System.Array.IndexOf(args, "--") + 1)..] : [];
            if (args[0] == "profile")
            {
                int samples = int.Parse(Option("samples", "5"), CultureInfo.InvariantCulture);
                if (samples is < 1 or > 30) throw new ArgumentException("samples must be between 1 and 30");
                Profile(args[1], output, frames, samples, Option("v-log", ""));
            }
            else
            {
                Engine engine = Enum.Parse<Engine>(Option("engine", "optimized"), true);
                var im = Image.Load(args[1]);
                if (engine != Engine.Il) MipsCompiler.Compile(im, engine == Engine.Optimized);
                var vm = new Vm(im, engine, frames);
                try { vm.Run(programArgs); } catch { Console.Write(vm.Output); throw; }
                Console.Write(vm.Output);
                foreach (var f in vm.Host.Frames) Console.WriteLine($"FRAME {f.Number} lines={f.Lines} rects={f.Rects} texts={f.Texts} geometry={f.Geometry:x8} work_ms={f.Milliseconds:F4} guest={f.Guest} bridges={f.Bridges}");
                Console.WriteLine("PROLANG OK");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"PROLANG FAIL: {ex.Message}"); return 1; }
    }

    sealed record Measurement(Engine Engine, Vm Validation, double[] Samples, double LoadMs, double CompileMs, long Allocated, int Gen0, int Gen1, int Gen2);
    static double Median(IEnumerable<double> numbers)
    {
        double[] a = numbers.Order().ToArray();
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }
    static void Profile(string path, string output, int frames, int samples, string vLog)
    {
        Directory.CreateDirectory(output);
        var results = new List<Measurement>();
        foreach (Engine engine in Enum.GetValues<Engine>())
        {
            var timer = Stopwatch.StartNew(); var im = Image.Load(path); double load = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); if (engine != Engine.Il) MipsCompiler.Compile(im, engine == Engine.Optimized);
            double compile = timer.Elapsed.TotalMilliseconds;
            var validation = new Vm(im, engine, frames); validation.Host.Capture = true; validation.Run([]);
            ValidateCube(validation, frames);
            if (results.Count > 0) Compare(results[0].Validation, validation);
            // Warm the same mode with the complete workload before recording any timings.
            new Vm(im, engine, frames).Run([]);
            var timings = new List<double>(); long bytes = 0; int g0 = 0, g1 = 0, g2 = 0;
            for (int sample = 0; sample < samples; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
                long allocated = GC.GetTotalAllocatedBytes(true);
                var vm = new Vm(im, engine, frames); vm.Run([]);
                ValidateCube(vm, frames); Compare(validation, vm);
                bytes += GC.GetTotalAllocatedBytes(true) - allocated;
                g0 += GC.CollectionCount(0) - c0; g1 += GC.CollectionCount(1) - c1; g2 += GC.CollectionCount(2) - c2;
                timings.Add(Median(vm.Host.Frames.Skip(1).Select(f => f.Milliseconds)));
            }
            results.Add(new(engine, validation, timings.ToArray(), load, compile, bytes / samples, g0, g1, g2));
            WriteListings(im, output, engine.ToString().ToLowerInvariant());
            Console.WriteLine($"PASS {engine}: {frames} frames; median work {Median(timings):F4} ms; guest words/frame {validation.Host.Frames.Skip(1).Average(f => f.Guest):F0}");
        }
        var baseline = results[0].Validation;
        string reference = "V reference not supplied; only the C# engines were compared.";
        if (vLog.Length > 0)
        {
            var matches = Regex.Matches(File.ReadAllText(vLog), @"FRAME (\d+) lines=(\d+) rects=(\d+) texts=(\d+) geometry=([0-9a-f]+)");
            int compared = Math.Min(matches.Count, baseline.Host.Frames.Count);
            if (compared < 2) throw new InvalidDataException("V reference contains fewer than two frames");
            for (int j = 0; j < compared; j++)
            {
                var g = matches[j].Groups; var f = baseline.Host.Frames[j];
                if (int.Parse(g[1].Value, CultureInfo.InvariantCulture) != f.Number || int.Parse(g[2].Value, CultureInfo.InvariantCulture) != f.Lines || int.Parse(g[3].Value, CultureInfo.InvariantCulture) != f.Rects || int.Parse(g[4].Value, CultureInfo.InvariantCulture) != f.Texts || uint.Parse(g[5].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != f.Geometry)
                    throw new InvalidDataException($"V reference differs at frame {j + 1}");
            }
            reference = $"PASS: {compared} frames match V/PPSSPP draw counts and every edge geometry hash. Reference SHA256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(vLog)))}. This reference is historical; it was not re-run by this command.";
            File.Copy(vLog, Path.Combine(output, "v-reference.log"), true);
        }
        File.WriteAllText(Path.Combine(output, "first-frame.svg"), baseline.Host.FirstSvg);
        File.WriteAllText(Path.Combine(output, "last-frame.svg"), baseline.Host.LastSvg);
        var report = new StringBuilder();
        string mode = RuntimeFeature.IsDynamicCodeSupported ? "CoreCLR JIT host" : "NativeAOT host (no host JIT)";
        report.AppendLine($"ProLang C# MSIL -> MIPS runtime profile\n{mode}\n{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}\nUTC: {DateTimeOffset.UtcNow:O}\nAssembly SHA256: {baseline.Image.Sha256}\nFrames/run: {frames}; measured runs/mode: {samples}; one full warmup/mode; first frame excluded.\n");
        report.AppendLine("TIMING BOUNDARY: headless CPU work between presents, on this desktop, with method counters enabled. No PSP CPU/GPU, rasterization, display/vblank, real input or real clock. TimeOps is a deterministic 17 ms/frame clock; Sleep/Vsync are test no-ops. Timings exclude image loading, MIPS compilation, SVG generation, report writing and forced GC between samples. Allocations include VM setup. These are not PSP FPS estimates.\n");
        report.AppendLine(reference + "\n");
        report.AppendLine("Engine       median ms  min ms  max ms  allocated/run  MIPS/frame  bridges/frame  code bytes  load ms  compile ms  GC(0/1/2)");
        foreach (var r in results)
        {
            var steady = r.Validation.Host.Frames.Skip(1).ToArray();
            report.AppendLine($"{r.Engine,-12} {Median(r.Samples),9:F4} {r.Samples.Min(),7:F4} {r.Samples.Max(),7:F4} {r.Allocated,14} {steady.Average(f => f.Guest),11:F0} {steady.Average(f => f.Bridges),14:F0} {r.Validation.Image.Methods.Sum(m => m.Words.Length * 4),11} {r.LoadMs,8:F2} {r.CompileMs,11:F2} {r.Gen0}/{r.Gen1}/{r.Gen2}");
        }
        var unopt = results[1]; var opt = results[2];
        double reduction = 100 * (1 - (double)opt.Validation.GuestInstructions / unopt.Validation.GuestInstructions);
        report.AppendLine($"\nOPTIMIZATIONS\n1. Decode metadata, resolve calls/strings/fields once; verify evaluation stack depths before execution.\n2. Emit MIPS loads/stores, integer arithmetic and branches with fixed frame offsets and integer-kind checks. Complex operations use counted syscall services.\n3. Optimized mode folds calls to parameterless integer constant methods ({opt.Validation.Image.Methods.Sum(m => m.FoldedCalls)} static call sites) and fuses constant+arithmetic pairs ({opt.Validation.Image.Methods.Sum(m => m.FusedPairs)} static pairs), never across a branch target.\nMeasured total MIPS instruction reduction: {reduction:F2}%. Measured host work ratio (Mips/Optimized): {Median(unopt.Samples) / Median(opt.Samples):F3}x. A ratio below 1 means slower. Emulated MIPS adds dispatch overhead; it is not expected to beat direct IL interpretation on this host.\n");
        report.AppendLine("TOP METHODS (optimized mode; dynamic entry counts / dispatched IL groups, inclusive of initialization; not time samples)");
        foreach (int n in Enumerable.Range(0, baseline.Image.Methods.Length).OrderByDescending(n => opt.Validation.Instructions[n]).Take(20))
            report.AppendLine($"{opt.Validation.Instructions[n],12} operations {opt.Validation.Calls[n],10} calls  {baseline.Image.Methods[n].Name}");
        report.AppendLine("\nLISTINGS\nil.txt: original guest MSIL and verified stack depths.\nmips.txt / optimized.txt: actual emitted MIPS words, mnemonics and IL mapping; *.mips.bin contains concatenated little-endian method code (offsets shown in listings).\nhost-jit.txt: captured by profile.ps1 from the separate CoreCLR host; not MIPS.\nhost-aot.txt: captured by profile.ps1 from the NativeAOT executable; not a JIT.\n\nLIMITS\nExperimental compatibility profile, not a CLR or an untrusted-code sandbox. No classes, exceptions, int64/floats, arbitrary external assemblies, or general generics. MIPS uses a desktop service ABI, not PSP EABI; binaries are inspection artifacts, not PSP executables. Object handles retain temporary values until a run ends (2 million handle cap); arrays have a 262144 cumulative element limit. Frame/value stack and instruction budgets are checked. SVG text approximates the PSP bitmap font.\n");
        File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString());
        string html = "<!doctype html><meta charset='utf-8'><title>ProLang C# MIPS profile</title><style>body{background:#111827;color:#e5e7eb;font:15px system-ui;margin:2rem;max-width:1200px}pre{white-space:pre-wrap;background:#1f2937;padding:1.5rem;line-height:1.5}a{color:#67e8f9}img{width:480px;max-width:100%;border:1px solid #475569}</style><h1>ProLang C# MIPS runtime</h1><p>Cube geometry: all three engines agree.</p><img src='first-frame.svg' alt='First cube frame'> <img src='last-frame.svg' alt='Last cube frame'><p><a href='il.txt'>Guest IL</a> · <a href='mips.txt'>MIPS baseline</a> · <a href='optimized.txt'>Optimized MIPS</a> · <a href='host-jit.txt'>Host JIT</a> · <a href='host-aot.txt'>Host AOT</a> · <a href='frames.csv'>Frame data</a> · <a href='samples.csv'>Measured samples</a></p><pre>" + WebUtility.HtmlEncode(report.ToString()) + "</pre>";
        File.WriteAllText(Path.Combine(output, "report.html"), html);
        File.WriteAllLines(Path.Combine(output, "samples.csv"), new[] { "engine,sample,median_frame_ms" }.Concat(results.SelectMany(r => r.Samples.Select((v, n) => FormattableString.Invariant($"{r.Engine},{n + 1},{v:F6}")))));
        File.WriteAllLines(Path.Combine(output, "frames.csv"), new[] { "engine,frame,lines,rects,texts,geometry,validation_ms,operations,mips_instructions,services,allocated_bytes" }.Concat(results.SelectMany(r => r.Validation.Host.Frames.Select(f => FormattableString.Invariant($"{r.Engine},{f.Number},{f.Lines},{f.Rects},{f.Texts},{f.Geometry:x8},{f.Milliseconds:F6},{f.Steps},{f.Guest},{f.Bridges},{f.Allocated}")))));
        Console.WriteLine(report.ToString());
        Console.WriteLine("Report: " + Path.Combine(output, "report.html"));
    }

    static void ValidateCube(Vm vm, int frames)
    {
        if (vm.Host.Frames.Count != frames) throw new InvalidOperationException($"expected {frames} frames, got {vm.Host.Frames.Count}");
        if (vm.Host.Frames.Any(f => f.Lines != 12 || f.Rects < 4 || f.Texts < 4)) throw new InvalidOperationException("cube drawing is incomplete");
        if (vm.Host.Frames.Select(f => f.Geometry).Distinct().Count() < 2) throw new InvalidOperationException("cube did not rotate");
    }
    static void Compare(Vm a, Vm b)
    {
        if (a.Output != b.Output || a.Host.Frames.Count != b.Host.Frames.Count || a.Host.Frames.Zip(b.Host.Frames).Any(p => (p.First.Geometry, p.First.Lines, p.First.Rects, p.First.Texts) != (p.Second.Geometry, p.Second.Lines, p.Second.Rects, p.Second.Texts)))
            throw new InvalidOperationException("differential execution mismatch");
    }
    static void WriteListings(Image image, string output, string mode)
    {
        var il = new StringBuilder(); var asm = new StringBuilder();
        using var bin = File.Create(Path.Combine(output, mode + ".mips.bin"));
        byte[] buffer = new byte[4];
        foreach (var m in image.Methods)
        {
            il.AppendLine($"\n{m.Site} maxstack={m.MaxStack} locals={m.Locals.Length}");
            foreach (var i in m.Code) il.AppendLine($"  stack={i.Depth,3}  {i}");
            asm.AppendLine($"\n{m.Name} file-offset=0x{bin.Position:x8} bytes={m.Words.Length * 4} folded={m.FoldedCalls} fused={m.FusedPairs}");
            for (int n = 0; n < m.Words.Length; n++)
            {
                asm.AppendLine($"  {n * 4:x8}  {m.Words[n]:x8}  {m.Assembly[n],-62} ; IL_{m.Code[m.WordIl[n]].Offset:x4}");
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, m.Words[n]); bin.Write(buffer);
            }
        }
        File.WriteAllText(Path.Combine(output, "il.txt"), il.ToString());
        if (mode != "il") File.WriteAllText(Path.Combine(output, mode + ".txt"), asm.ToString());
    }
}
