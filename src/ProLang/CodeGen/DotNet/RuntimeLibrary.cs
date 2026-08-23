using Mono.Cecil;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// Locates <c>ProLang.Runtime.dll</c> and names the members the emitter calls into it.
/// </summary>
/// <remarks>
/// <para>
/// The runtime assembly holds the builtins that used to be hand-emitted IL: output buffering,
/// the console operations and their redirection guards, and the string helpers whose ProLang
/// semantics differ from .NET's. The emitter now emits a plain <c>call</c> to each.
/// </para>
/// <para>
/// It is read with Cecil, never loaded into the compiler's own process, so the compiler is not
/// exposed to anything it does. It lives in <c>runtime/</c> beside the compiler and is copied
/// next to each emitted program by <c>ProLangCompilation.Emit</c>.
/// </para>
/// </remarks>
internal static class RuntimeLibrary
{
    /// <summary>Assembly file name, used for both probing and deployment.</summary>
    public const string AssemblyFileName = "ProLang.Runtime.dll";

    /// <summary>Metadata full name of the output-buffering type.</summary>
    public const string Output = "ProLang.Runtime.Output";

    /// <summary>Metadata full name of the console operations type.</summary>
    public const string ConsoleOps = "ProLang.Runtime.ConsoleOps";

    /// <summary>Metadata full name of the string operations type.</summary>
    public const string StringOps = "ProLang.Runtime.StringOps";

    /// <summary>Metadata full name of the numeric operations type.</summary>
    public const string MathOps = "ProLang.Runtime.MathOps";

    /// <summary>Metadata full name of the assertion type backing <c>import "test"</c>.</summary>
    public const string TestOps = "ProLang.Runtime.TestOps";

    /// <summary>Metadata full name of the monotonic clock behind <c>time_millis</c>.</summary>
    public const string TimeOps = "ProLang.Runtime.TimeOps";

    /// <summary>
    /// Finds <c>ProLang.Runtime.dll</c> on disk.
    /// </summary>
    /// <returns>Its full path, or <see langword="null"/> if it is not deployed.</returns>
    /// <remarks>
    /// <para>
    /// Probes <c>runtime/</c> and then the directory itself, under two roots: the location of the
    /// compiler assembly, and <see cref="AppContext.BaseDirectory"/>.
    /// </para>
    /// <para>
    /// Both roots are needed because the two differ whenever the compiler is driven as a library
    /// rather than as the CLI. Under <c>dotnet test</c> the base directory is the test host's;
    /// under BenchmarkDotNet it is a generated project's. Anchoring on the compiler assembly's
    /// own location finds the runtime in those cases too.
    /// </para>
    /// </remarks>
    public static string? FindAssemblyPath()
    {
        foreach (var root in ProbeRoots())
        {
            var inRuntimeFolder = Path.Combine(root, "runtime", AssemblyFileName);

            if (File.Exists(inRuntimeFolder))
            {
                return inRuntimeFolder;
            }

            var alongside = Path.Combine(root, AssemblyFileName);

            if (File.Exists(alongside))
            {
                return alongside;
            }
        }

        return null;
    }

    /// <summary>
    /// Maximum number of parent directories searched above each starting point.
    /// </summary>
    /// <remarks>
    /// BenchmarkDotNet builds and runs each benchmark from a project it generates *inside* the
    /// host's output directory, so the runtime sits several levels above where the code is
    /// executing. Four levels covers that without wandering far enough to pick up an unrelated
    /// copy.
    /// </remarks>
    private const int MaxParentDirectoriesToSearch = 4;

    private static IEnumerable<string> ProbeRoots()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(RuntimeLibrary).Assembly.Location),
            AppContext.BaseDirectory,
        };

        foreach (var start in starts)
        {
            if (string.IsNullOrEmpty(start))
            {
                continue;
            }

            var directory = new DirectoryInfo(start);

            for (var level = 0; level <= MaxParentDirectoriesToSearch && directory != null; level++)
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }
    }

    /// <summary>
    /// Reads the runtime assembly so its members can be resolved.
    /// </summary>
    /// <returns>
    /// The assembly, or <see langword="null"/> if it is missing or unreadable. A null result is
    /// not fatal at construction time — it surfaces as a diagnostic from the first builtin that
    /// needs it, which names the missing member rather than just the missing file.
    /// </returns>
    public static AssemblyDefinition? TryLoad()
    {
        var path = FindAssemblyPath();

        if (path == null)
        {
            return null;
        }

        try
        {
            return AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
        }
        catch (Exception)
        {
            return null;
        }
    }
}
