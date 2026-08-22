namespace ProLang.Interop;

/// <summary>
/// The outcome of resolving an <c>assembly:</c> import or a <c>-r</c> reference.
/// </summary>
/// <param name="Path">Full path of the assembly, or <see langword="null"/> if it was not found.</param>
/// <param name="ProbedLocations">Where the resolver looked, in order, for the failure message.</param>
/// <param name="Suggestions">Assemblies with a similar name that were found nearby.</param>
internal sealed record AssemblyResolution(
    string? Path,
    IReadOnlyList<string> ProbedLocations,
    IReadOnlyList<string> Suggestions)
{
    public bool Found => Path != null;
}

/// <summary>
/// Locates a .NET assembly named by an <c>assembly:</c> import.
/// </summary>
/// <remarks>
/// <para>
/// Referencing a .NET library used to mean giving an exact path to a built <c>.dll</c>, which in
/// practice meant hardcoding a build configuration and target framework —
/// <c>assembly:test_lib/bin/Debug/net10.0/MyLib.dll</c> — and re-editing the source whenever
/// either changed. This accepts the shapes people actually want to write:
/// </para>
/// <list type="bullet">
///   <item><description><c>assembly:MyLib</c> — a bare name, found by searching build output nearby.</description></item>
///   <item><description><c>assembly:MyLib.dll</c> — same, with the extension spelled out.</description></item>
///   <item><description><c>assembly:libs/MyLib.dll</c> — an explicit relative or absolute path.</description></item>
///   <item><description><c>assembly:test_lib/MyLib.csproj</c> — a C# project, resolved to its build output.</description></item>
/// </list>
/// <para>
/// When nothing matches, the caller gets the list of locations probed and any near-miss names, so
/// the diagnostic can say where it looked rather than only what it wanted.
/// </para>
/// </remarks>
internal static class AssemblyReferenceResolver
{
    /// <summary>
    /// How deep to search for build output below a starting directory.
    /// </summary>
    /// <remarks>
    /// Deep enough for the conventional <c>&lt;project&gt;/bin/&lt;config&gt;/&lt;tfm&gt;/</c>
    /// layout from a directory holding several projects, shallow enough not to walk a large
    /// source tree.
    /// </remarks>
    private const int MaxSearchDepth = 5;

    /// <summary>Directories never worth searching for a referenced assembly.</summary>
    private static readonly string[] SkippedDirectories = ["obj", ".git", "node_modules", ".vs", ".prolang"];

    /// <summary>
    /// Resolves <paramref name="request"/> to an assembly path.
    /// </summary>
    /// <param name="request">The text after <c>assembly:</c>, or a <c>-r</c> argument.</param>
    /// <param name="importingDirectory">Directory of the file containing the import, if any.</param>
    /// <param name="compilerLibDirectory">The compiler's <c>lib/</c> directory, for bundled assemblies.</param>
    public static AssemblyResolution Resolve(
        string request,
        string? importingDirectory,
        string? compilerLibDirectory)
    {
        var probed = new List<string>();

        // A project reference resolves through its build output, which needs its own probing.
        if (request.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveProject(request, importingDirectory, probed);
        }

        var searchRoots = SearchRoots(importingDirectory);

        // 1. The path as written, relative to the importing file, the working directory, or
        //    absolute. This is the explicit case and wins over any search.
        foreach (var root in searchRoots)
        {
            if (TryFile(Path.Combine(root, request), probed, out var direct))
            {
                return Found(direct, probed);
            }
        }

        if (Path.IsPathRooted(request) && TryFile(request, probed, out var absolute))
        {
            return Found(absolute, probed);
        }

        var fileName = request.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? request
            : request + ".dll";

        // 2. The same, with a .dll extension added.
        if (!ReferenceEquals(fileName, request))
        {
            foreach (var root in searchRoots)
            {
                if (TryFile(Path.Combine(root, fileName), probed, out var withExtension))
                {
                    return Found(withExtension, probed);
                }
            }
        }

        // 3. The compiler's own lib/ directory, so bundled helpers work by bare name.
        if (compilerLibDirectory != null
            && !request.Contains('/') && !request.Contains('\\')
            && TryFile(Path.Combine(compilerLibDirectory, Path.GetFileName(fileName)), probed, out var bundled))
        {
            return Found(bundled, probed);
        }

        // 4. Build output nearby. This is what removes the hardcoded bin/<config>/<tfm> path.
        var bareName = Path.GetFileName(fileName);

        foreach (var root in searchRoots)
        {
            probed.Add(Path.Combine(root, "**", bareName));

            var match = FindBuildOutput(root, bareName);

            if (match != null)
            {
                return Found(match, probed);
            }
        }

        return new AssemblyResolution(null, probed, CollectSuggestions(searchRoots, bareName));
    }

    /// <summary>
    /// Resolves a <c>.csproj</c> reference to the assembly its build produces.
    /// </summary>
    private static AssemblyResolution ResolveProject(
        string request,
        string? importingDirectory,
        List<string> probed)
    {
        string? projectPath = null;

        foreach (var root in SearchRoots(importingDirectory))
        {
            if (TryFile(Path.Combine(root, request), probed, out var candidate))
            {
                projectPath = candidate;
                break;
            }
        }

        if (projectPath == null && Path.IsPathRooted(request) && TryFile(request, probed, out var absolute))
        {
            projectPath = absolute;
        }

        if (projectPath == null)
        {
            return new AssemblyResolution(null, probed, []);
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var assemblyName = Path.GetFileNameWithoutExtension(projectPath) + ".dll";

        probed.Add(Path.Combine(projectDirectory, "bin", "**", assemblyName));

        var output = FindBuildOutput(projectDirectory, assemblyName);

        return output != null
            ? Found(output, probed)
            // The project exists but has not been built. That is a different problem from a
            // missing reference, and the caller's diagnostic says so.
            : new AssemblyResolution(null, probed, []);
    }

    /// <summary>
    /// Finds <paramref name="assemblyFileName"/> in build output below <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Paths under a <c>bin</c> directory are preferred, and <c>Release</c> over <c>Debug</c>, so
    /// that a stale copy left elsewhere in the tree does not win over a real build. Ties are
    /// broken by last-write time.
    /// </remarks>
    private static string? FindBuildOutput(string root, string assemblyFileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        var matches = new List<string>();
        Search(root, depth: 0);

        return matches
            .OrderByDescending(p => p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        void Search(string directory, int depth)
        {
            if (depth > MaxSearchDepth)
            {
                return;
            }

            try
            {
                var candidate = Path.Combine(directory, assemblyFileName);

                if (File.Exists(candidate) && FileNameMatchesCase(candidate))
                {
                    matches.Add(candidate);
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(child);

                    if (!SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        Search(child, depth + 1);
                    }
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // A directory we cannot read is not worth failing the whole resolution over.
            }
        }
    }

    /// <summary>
    /// Finds assemblies whose name is close to what was asked for.
    /// </summary>
    /// <remarks>
    /// Catches the case this was written for: a reference to <c>CSharpFIbonacci.dll</c> when the
    /// assembly on disk is <c>CSharpFibonacci.dll</c>. A case-only difference is invisible on
    /// Windows but fatal on Linux, and neither spelling looks wrong at a glance.
    /// </remarks>
    private static List<string> CollectSuggestions(IEnumerable<string> roots, string wanted)
    {
        var suggestions = new List<string>();
        var wantedStem = Path.GetFileNameWithoutExtension(wanted);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var found in EnumerateAssemblies(root, depth: 0))
            {
                var stem = Path.GetFileNameWithoutExtension(found);

                if (string.Equals(stem, wantedStem, StringComparison.OrdinalIgnoreCase)
                    || IsNearMiss(stem, wantedStem))
                {
                    var name = Path.GetFileName(found);

                    if (!suggestions.Contains(name, StringComparer.Ordinal))
                    {
                        suggestions.Add(name);
                    }
                }
            }
        }

        return suggestions;
    }

    private static IEnumerable<string> EnumerateAssemblies(string directory, int depth)
    {
        if (depth > MaxSearchDepth)
        {
            yield break;
        }

        string[] files;
        string[] children;

        try
        {
            files = Directory.GetFiles(directory, "*.dll");
            children = Directory.GetDirectories(directory);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }

        foreach (var child in children)
        {
            if (SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var nested in EnumerateAssemblies(child, depth + 1))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Whether two names differ by at most one or two characters.</summary>
    private static bool IsNearMiss(string candidate, string wanted)
    {
        if (Math.Abs(candidate.Length - wanted.Length) > 2)
        {
            return false;
        }

        var distance = EditDistance(candidate.ToLowerInvariant(), wanted.ToLowerInvariant());

        // Scale the tolerance with length so short names do not match everything.
        return distance > 0 && distance <= Math.Max(1, Math.Min(2, wanted.Length / 4));
    }

    private static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Directories a relative reference is resolved against, in priority order.</summary>
    private static List<string> SearchRoots(string? importingDirectory)
    {
        var roots = new List<string>();

        if (!string.IsNullOrEmpty(importingDirectory))
        {
            roots.Add(importingDirectory);
        }

        var workingDirectory = Directory.GetCurrentDirectory();

        if (!roots.Contains(workingDirectory, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(workingDirectory);
        }

        return roots;
    }

    private static bool TryFile(string candidate, List<string> probed, out string resolved)
    {
        var full = Path.GetFullPath(candidate);
        probed.Add(full);

        if (File.Exists(full) && FileNameMatchesCase(full))
        {
            resolved = full;
            return true;
        }

        resolved = string.Empty;
        return false;
    }

    /// <summary>
    /// Whether the file name on disk matches the requested spelling exactly, including case.
    /// </summary>
    /// <remarks>
    /// Windows paths are case-insensitive, so <c>CSharpFIbonacci.dll</c> happily resolves to
    /// <c>CSharpFibonacci.dll</c> there and then fails on Linux, where the same source is
    /// suddenly a missing reference. Matching case here makes the two platforms agree and turns
    /// the typo into a diagnostic that suggests the right spelling.
    /// <para>
    /// Only the file name is checked. Directory casing is left to the filesystem — being strict
    /// about a whole hand-typed path would reject far more than it caught.
    /// </para>
    /// </remarks>
    private static bool FileNameMatchesCase(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);

        if (directory == null)
        {
            return true;
        }

        try
        {
            var requested = Path.GetFileName(fullPath);
            var actual = Directory.EnumerateFiles(directory, requested).FirstOrDefault();

            return actual == null || Path.GetFileName(actual).Equals(requested, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            // Cannot verify; accept what File.Exists already said rather than fail the build.
            return true;
        }
    }

    private static AssemblyResolution Found(string path, List<string> probed) => new(path, probed, []);
}
