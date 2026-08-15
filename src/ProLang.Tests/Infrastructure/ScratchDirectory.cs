namespace ProLang.Tests.Infrastructure;

/// <summary>
/// A disposable temporary directory for build output produced by a single test.
/// </summary>
/// <remarks>
/// Compiled assemblies must not land in the source tree — the repository already carried
/// accidentally-committed <c>.dll</c> files from earlier script-based test runs.
/// </remarks>
internal sealed class ScratchDirectory : IDisposable
{
    /// <summary>Parent of every scratch directory, so stale ones can be found and swept.</summary>
    private static readonly string RootDirectory =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prolang-tests");

    /// <summary>
    /// Scratch directories older than this are assumed to be leftovers from a previous run and
    /// are deleted on first use. Generous enough not to touch a concurrently running suite.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private static readonly Lock SweepGate = new();
    private static bool _sweptStaleDirectories;

    private ScratchDirectory(string path) => Path = path;

    /// <summary>Absolute path of the directory. It exists for the lifetime of this object.</summary>
    public string Path { get; }

    /// <summary>Creates a uniquely named directory under the system temp folder.</summary>
    /// <param name="label">Short prefix to make the directory identifiable while debugging.</param>
    public static ScratchDirectory Create(string label)
    {
        SweepStaleDirectoriesOnce();

        var safeLabel = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var path = System.IO.Path.Combine(RootDirectory, $"{safeLabel}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        return new ScratchDirectory(path);
    }

    /// <summary>Combines a file name onto this directory.</summary>
    public string File(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose() => TryDelete(Path, attempts: 3);

    /// <summary>
    /// Deletes a directory, retrying briefly on the transient locks Windows leaves behind.
    /// </summary>
    /// <remarks>
    /// Tests run the emitted assembly in a child <c>dotnet</c> process. Windows can hold the
    /// image lock for a short window after that process exits, so the first delete often fails
    /// with <see cref="IOException"/> even though nothing is really using the file. Giving up on
    /// the first failure is what let hundreds of directories accumulate in temp.
    /// </remarks>
    private static void TryDelete(string path, int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == attempts - 1)
                {
                    // Still locked. The startup sweep will collect it on a later run rather than
                    // failing an otherwise-passing test over a temp directory.
                    return;
                }

                Thread.Sleep(millisecondsTimeout: 50 * (attempt + 1));
            }
        }
    }

    /// <summary>
    /// Removes scratch directories left behind by earlier runs, once per process.
    /// </summary>
    /// <remarks>
    /// The retry in <see cref="TryDelete"/> handles the common case, but a killed test host or a
    /// still-locked assembly can always strand one. Sweeping on startup keeps that bounded
    /// instead of letting temp grow without limit.
    /// </remarks>
    private static void SweepStaleDirectoriesOnce()
    {
        lock (SweepGate)
        {
            if (_sweptStaleDirectories)
            {
                return;
            }

            _sweptStaleDirectories = true;

            if (!Directory.Exists(RootDirectory))
            {
                Directory.CreateDirectory(RootDirectory);
                return;
            }

            var cutoff = DateTime.UtcNow - StaleAfter;

            foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(directory) < cutoff)
                    {
                        TryDelete(directory, attempts: 1);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Another run may own it. Leave it alone.
                }
            }
        }
    }
}
