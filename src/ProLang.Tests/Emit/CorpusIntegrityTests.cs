using ProLang.Tests.Infrastructure;

namespace ProLang.Tests.Emit;

/// <summary>
/// Keeps <see cref="TestCorpus"/> honest.
/// </summary>
/// <remarks>
/// The corpus is an explicit list rather than a glob, which means it can drift out of sync with
/// the repository. These tests make drift a failure instead of a silent coverage gap — which is
/// exactly how the previous script harness lost track of the eight <c>tests/*.prl</c> files that
/// sat outside the directory it globbed.
/// </remarks>
public sealed class CorpusIntegrityTests
{
    [Fact]
    public void EveryProLangSourceInTheRepository_IsClassified()
    {
        var onDisk = DiscoverSourceFiles();
        var classified = TestCorpus.All.Select(e => e.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unclassified = onDisk.Where(p => !classified.Contains(p)).OrderBy(p => p).ToList();

        Assert.True(
            unclassified.Count == 0,
            $"These .prl files exist but are not in TestCorpus, so nothing covers them:{Environment.NewLine}" +
            string.Join(Environment.NewLine, unclassified.Select(p => "  " + p)));
    }

    [Fact]
    public void EveryCorpusEntry_ExistsOnDisk()
    {
        var missing = TestCorpus.All
            .Where(e => !File.Exists(e.FullPath))
            .Select(e => e.RelativePath)
            .OrderBy(p => p)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These TestCorpus entries no longer exist on disk:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missing.Select(p => "  " + p)));
    }

    [Fact]
    public void EveryNonRunnableEntry_ExplainsWhy()
    {
        var unexplained = TestCorpus.All
            .Where(e => e.Kind != CorpusKind.Runnable && string.IsNullOrWhiteSpace(e.Note))
            .Select(e => e.RelativePath)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            $"These entries are excluded from full coverage without a stated reason:{Environment.NewLine}" +
            string.Join(Environment.NewLine, unexplained.Select(p => "  " + p)));
    }

    /// <summary>
    /// Asserts the known-broken programs are still broken.
    /// </summary>
    /// <remarks>
    /// A failure here is good news that still needs acting on: the program started compiling, so
    /// it should be promoted out of <see cref="CorpusKind.KnownBroken"/> and given real coverage.
    /// Without this test a fix would go unnoticed and the file would stay permanently untested.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KnownBroken))]
    public void KnownBrokenProgram_StillFailsToCompile(string relativePath)
    {
        var entry = TestCorpus.Get(relativePath);

        using var scratch = ScratchDirectory.Create(entry.Name);

        var compiled = false;
        try
        {
            compiled = CompilerHarness.CompileToFile(scratch.Path, entry.FullPath).Succeeded;
        }
        catch (Exception)
        {
            // Some of these crash the binder outright rather than reporting a diagnostic.
            // That still counts as "broken"; the reason is recorded on the corpus entry.
            return;
        }

        Assert.False(
            compiled,
            $"'{entry.RelativePath}' now compiles, but is still listed as KnownBroken " +
            $"({entry.Note}). Move it to Runnable or CompileOnly and capture its baseline.");
    }

    private static List<string> DiscoverSourceFiles()
    {
        var roots = new[] { TestPaths.Examples, TestPaths.TestCorpus };
        var results = new List<string>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.prl", SearchOption.AllDirectories))
            {
                // Generated transpiler output lands under .prolang/ next to the sources.
                if (file.Contains($"{Path.DirectorySeparatorChar}.prolang{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                results.Add(Path.GetRelativePath(TestPaths.RepoRoot, file).Replace('\\', '/'));
            }
        }

        return results;
    }

    public static TheoryData<string> KnownBroken => TestCorpus.KnownBrokenData;
}
