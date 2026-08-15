namespace ProLang.Tests.Infrastructure;

/// <summary>
/// Locates repository directories from the test assembly's output folder.
/// </summary>
/// <remarks>
/// Tests run out of <c>src/ProLang.Tests/bin/&lt;config&gt;/net10.0/</c>, so the repository root is
/// found by walking upwards until a directory containing both <c>src/</c> and <c>examples/</c>
/// is seen. Anchoring on those two rather than on a fixed number of <c>..</c> segments keeps
/// this working under <c>dotnet test</c>, the VS test runner, and any future output layout.
/// </remarks>
internal static class TestPaths
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    /// <summary>Absolute path of the repository root.</summary>
    public static string RepoRoot => RepoRootLazy.Value;

    /// <summary>Absolute path of <c>examples/</c>.</summary>
    public static string Examples => Path.Combine(RepoRoot, "examples");

    /// <summary>Absolute path of <c>tests/</c> (the ProLang-source test corpus).</summary>
    public static string TestCorpus => Path.Combine(RepoRoot, "tests");

    /// <summary>
    /// Directory holding the checked-in golden files, resolved against the *source* tree rather
    /// than the build output so that snapshot updates land somewhere git can see them.
    /// </summary>
    public static string SnapshotsDirectory => Path.Combine(RepoRoot, "src", "ProLang.Tests", "Snapshots");

    /// <inheritdoc cref="SnapshotsDirectory"/>
    public static string ExpectedOutputDirectory => Path.Combine(RepoRoot, "src", "ProLang.Tests", "Expected");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "examples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root walking up from '{AppContext.BaseDirectory}'. " +
            "Expected an ancestor directory containing both 'src' and 'examples'.");
    }
}
