using Xunit.Sdk;

namespace ProLang.Tests.Infrastructure;

/// <summary>
/// Compares generated text against a checked-in golden file.
/// </summary>
/// <remarks>
/// <para>
/// Golden files are the safety net for the code generation refactor: every mechanical extraction
/// step must leave the emitted IL byte-identical, so a diff here is the signal that a step changed
/// behaviour when it was not supposed to.
/// </para>
/// <para>
/// Set <c>PROLANG_UPDATE_SNAPSHOTS=1</c> to rewrite the golden files instead of asserting against
/// them. Only do that at a checkpoint where output is *expected* to change, and review the diff.
/// </para>
/// </remarks>
internal static class Snapshot
{
    private const string UpdateEnvironmentVariable = "PROLANG_UPDATE_SNAPSHOTS";

    /// <summary>
    /// True when the run should rewrite golden files rather than assert against them.
    /// </summary>
    public static bool IsUpdating =>
        Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) is "1" or "true" or "TRUE";

    /// <summary>
    /// Asserts <paramref name="actual"/> matches the golden file at
    /// <paramref name="directory"/>/<paramref name="fileName"/>, or rewrites it when updating.
    /// </summary>
    public static void Verify(string directory, string fileName, string actual)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        // Normalise line endings so a snapshot captured on Windows still matches elsewhere.
        var normalised = Normalise(actual);

        if (IsUpdating)
        {
            File.WriteAllText(path, normalised);
            return;
        }

        if (!File.Exists(path))
        {
            throw new XunitException(
                $"No snapshot at '{path}'.{Environment.NewLine}" +
                $"Run with {UpdateEnvironmentVariable}=1 to create it, then review and commit the file." +
                $"{Environment.NewLine}Actual output was:{Environment.NewLine}{normalised}");
        }

        var expected = Normalise(File.ReadAllText(path));

        if (expected == normalised)
        {
            return;
        }

        throw new XunitException(
            $"Snapshot mismatch for '{fileName}'.{Environment.NewLine}" +
            $"  Golden file: {path}{Environment.NewLine}" +
            $"  Set {UpdateEnvironmentVariable}=1 to re-baseline (only when the change is intended)." +
            $"{Environment.NewLine}{Environment.NewLine}{DescribeFirstDifference(expected, normalised)}");
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n").TrimEnd() + "\n";

    /// <summary>
    /// Renders the first differing line with a little context. Whole-file diffs of a large IL
    /// listing are unreadable in test output; the first divergence is what matters.
    /// </summary>
    private static string DescribeFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var limit = Math.Min(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < limit; i++)
        {
            if (expectedLines[i] == actualLines[i])
            {
                continue;
            }

            var contextStart = Math.Max(0, i - 3);
            var context = string.Join(
                Environment.NewLine,
                expectedLines[contextStart..i].Select(l => $"   {l}"));

            return $"""
                First difference at line {i + 1}:
                {context}
                  - expected: {expectedLines[i]}
                  + actual:   {actualLines[i]}
                """;
        }

        return $"Files share their first {limit} lines but differ in length " +
               $"(expected {expectedLines.Length} lines, got {actualLines.Length}).";
    }
}
