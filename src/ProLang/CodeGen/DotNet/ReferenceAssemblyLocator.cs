using Mono.Cecil;

namespace ProLang.CodeGen.DotNet;

/// <summary>
/// Finds the .NET reference assemblies the emitter resolves BCL types against, and reads them
/// with Cecil.
/// </summary>
/// <remarks>
/// <para>
/// The emitter needs Cecil <see cref="TypeReference"/>s for BCL types, which means reading real
/// assembly files. The runtime does not expose a single canonical location for these, so this
/// probes four in order of preference:
/// </para>
/// <list type="number">
///   <item><description>The shared framework — <c>Program Files\dotnet\shared\Microsoft.NETCore.App\&lt;version&gt;</c>.</description></item>
///   <item><description>The SDK reference pack — <c>Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\&lt;version&gt;\ref\&lt;tfm&gt;</c>.</description></item>
///   <item><description>A user-local reference pack under <c>~/.dotnet/packs</c>.</description></item>
///   <item><description>The directory the compiler itself is running from.</description></item>
/// </list>
/// <para>
/// <b>Cost.</b> Reading this set — <c>System.Private.CoreLib.dll</c> in particular — is the single
/// most expensive thing the .NET backend does: roughly 9 ms and 7.4 MB per emitter instance,
/// which measurements put at about 99% of total compile time for small programs. See
/// <c>docs/perf/baseline-2026-08-15.md</c>. Extracting it here is what makes caching or lazy
/// loading a contained change rather than surgery on the emitter.
/// </para>
/// </remarks>
internal static class ReferenceAssemblyLocator
{
    /// <summary>
    /// Assemblies loaded for every compilation, whether or not the program imports anything.
    /// </summary>
    /// <remarks>
    /// <c>System.Private.CoreLib</c> must come first: it holds the actual definitions of the core
    /// types, while <c>System.Runtime</c> and friends only forward to it. Resolution walks this
    /// list in order and takes the first match, so a forwarding facade appearing first would
    /// yield a type reference with no members on it.
    /// </remarks>
    private static readonly string[] RequiredAssemblies =
    [
        "System.Private.CoreLib.dll",
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Collections.Concurrent.dll",
        "System.Linq.dll",
        "System.Text.RegularExpressions.dll",
        "System.IO.FileSystem.dll",
        "System.Threading.dll",
        "System.Net.Primitives.dll",
        "System.Text.Json.dll",
        "Microsoft.CSharp.dll",
    ];

    private static readonly Lock CacheGate = new();
    private static IReadOnlyList<AssemblyDefinition>? _cachedAssemblies;

    /// <summary>
    /// Reads the required runtime assemblies, skipping any that cannot be found or read.
    /// </summary>
    /// <returns>
    /// The loaded assemblies, deduplicated by simple name and ordered as
    /// <see cref="RequiredAssemblies"/>. Empty if no reference directory could be located.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The result is cached for the lifetime of the process. Reading this set costs roughly 9 ms
    /// and 7.4 MB — measurably around 99% of the time taken to compile a small program — and the
    /// reference assemblies cannot change while the compiler is running, so paying it once is
    /// both safe and a large win for anything that emits more than once: the test suite, the
    /// benchmarks, multi-file builds, and any future watch mode.
    /// </para>
    /// <para>
    /// Sharing <see cref="AssemblyDefinition"/> instances between emitters is safe because they
    /// are only ever read from. <c>ImportReference</c> mutates the *target* module, never the
    /// source definition, and Cecil's own lazy metadata caches are then shared too rather than
    /// being rebuilt per emit. Callers must not dispose what they get back.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AssemblyDefinition> LoadRuntimeAssemblies()
    {
        if (_cachedAssemblies != null)
        {
            return _cachedAssemblies;
        }

        lock (CacheGate)
        {
            return _cachedAssemblies ??= ReadRuntimeAssemblies();
        }
    }

    /// <summary>
    /// Drops the cached assemblies so the next call re-reads them from disk.
    /// </summary>
    /// <remarks>Exists for tests that need to observe the cold-start path.</remarks>
    internal static void ClearCache()
    {
        lock (CacheGate)
        {
            _cachedAssemblies = null;
        }
    }

    private static List<AssemblyDefinition> ReadRuntimeAssemblies()
    {
        var assemblies = new List<AssemblyDefinition>(RequiredAssemblies.Length);
        var directory = FindReferenceDirectory();

        if (directory == null || !Directory.Exists(directory))
        {
            return assemblies;
        }

        var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in RequiredAssemblies)
        {
            var path = Path.Combine(directory, fileName);

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });

                if (loadedNames.Add(assembly.Name.Name))
                {
                    assemblies.Add(assembly);
                }
            }
            catch (Exception)
            {
                // A reference assembly we cannot read is not fatal — resolution will report a
                // diagnostic for any type that turns out to be missing as a result.
            }
        }

        return assemblies;
    }

    /// <summary>
    /// Probes the well-known install layouts for a directory of reference assemblies.
    /// </summary>
    /// <returns>The first directory found, or the compiler's own runtime directory as a fallback.</returns>
    private static string? FindReferenceDirectory() =>
        FindSharedFrameworkDirectory()
        ?? FindReferencePackDirectory(Environment.SpecialFolder.ProgramFiles)
        ?? FindReferencePackDirectory(Environment.SpecialFolder.UserProfile)
        ?? System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

    private static string? FindSharedFrameworkDirectory()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.NETCore.App");

        return LatestSubdirectory(root);
    }

    private static string? FindReferencePackDirectory(Environment.SpecialFolder folder)
    {
        // The user-profile layout nests the packs under ".dotnet"; the machine-wide one does not.
        var root = folder == Environment.SpecialFolder.UserProfile
            ? Path.Combine(Environment.GetFolderPath(folder), ".dotnet", "packs", "Microsoft.NETCore.App.Ref")
            : Path.Combine(Environment.GetFolderPath(folder), "dotnet", "packs", "Microsoft.NETCore.App.Ref");

        var version = LatestSubdirectory(root);

        if (version == null)
        {
            return null;
        }

        // A reference pack holds ref/<tfm>/, e.g. ref/net10.0/.
        return LatestSubdirectory(Path.Combine(version, "ref"));
    }

    /// <summary>
    /// Returns the lexicographically greatest subdirectory of <paramref name="root"/>, which for
    /// version-named directories is the newest, or <see langword="null"/> if there are none.
    /// </summary>
    private static string? LatestSubdirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.GetDirectories(root)
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
