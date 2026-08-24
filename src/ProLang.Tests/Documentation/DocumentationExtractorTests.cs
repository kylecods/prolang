using ProLang.Documentation;
using ProLang.Tests.Infrastructure;
using ProLang.Text;

namespace ProLang.Tests.Documentation;

/// <summary>
/// Recovering the documentation that was always in the source.
/// </summary>
public class DocumentationExtractorTests
{
    private static SourceText Text(string text) => SourceText.From(text, "test.prl");

    private static int OffsetOf(SourceText text, string needle) => text.ToString().IndexOf(needle, StringComparison.Ordinal);

    [Fact]
    public void ForDeclaration_TakesALineCommentAboveIt()
    {
        var text = Text("""
            // Absolute value.
            func util_abs(value: int) : int { return value }
            """);

        Assert.Equal("Absolute value.", DocumentationExtractor.ForDeclaration(text, OffsetOf(text, "func")));
    }

    [Fact]
    public void ForDeclaration_TakesARunOfLineComments()
    {
        var text = Text("""
            // Constrains value to the inclusive range low..high.
            // Used by the layout pass.
            func util_clamp(value: int) : int { return value }
            """);

        Assert.Equal(
            "Constrains value to the inclusive range low..high.\nUsed by the layout pass.",
            DocumentationExtractor.ForDeclaration(text, OffsetOf(text, "func")));
    }

    [Fact]
    public void ForDeclaration_TakesABlockComment()
    {
        var text = Text("""
            /*
             * Integer division that rounds toward negative infinity.
             *
             * Screen-to-image mapping needs this.
             */
            func util_floor_div(a: int, b: int) : int { return a }
            """);

        Assert.Equal(
            "Integer division that rounds toward negative infinity.\n\nScreen-to-image mapping needs this.",
            DocumentationExtractor.ForDeclaration(text, OffsetOf(text, "func")));
    }

    /// <summary>
    /// A section banner is not the documentation of whatever happens to follow it.
    /// </summary>
    /// <remarks>
    /// This is the case the "stop at a blank line" rule exists for. Every banner in
    /// <c>std/ui/</c> is written with a blank line under it, and without this rule every function
    /// after one would claim it as its own description.
    /// </remarks>
    [Fact]
    public void ForDeclaration_IgnoresASectionBannerSeparatedByABlankLine()
    {
        var text = Text("""
            // ── Unpacking ────────────────────────────────────────────────

            func color_a(c: int) : int { return c }
            """);

        Assert.Null(DocumentationExtractor.ForDeclaration(text, OffsetOf(text, "func color_a")));
    }

    [Fact]
    public void ForDeclaration_IgnoresCodeAbove()
    {
        var text = Text("""
            func first() : int { return 1 }
            func second() : int { return 2 }
            """);

        Assert.Null(DocumentationExtractor.ForDeclaration(text, OffsetOf(text, "func second")));
    }

    [Fact]
    public void ForDeclaration_DropsARuleInsideABlockComment()
    {
        var text = Text("""
            /*
             * Draws a disc.
             * ────────────
             * Tested by squared distance.
             */
            func shape_disc() : int { return 1 }
            """);

        Assert.Equal(
            "Draws a disc.\n\nTested by squared distance.",
            DocumentationExtractor.ForDeclaration(text, OffsetOf(text, "func")));
    }

    [Fact]
    public void ForModule_TakesTheFileHeader()
    {
        var text = Text("""
            /*
             * Pixel editor — small integer helpers
             *
             * Everything here is pure and has no dependencies.
             */

            import "math"

            func util_abs(value: int) : int { return value }
            """);

        Assert.Equal(
            "Pixel editor — small integer helpers\n\nEverything here is pure and has no dependencies.",
            DocumentationExtractor.ForModule(text));
    }

    [Fact]
    public void ForModule_IsNullWhenTheFileOpensWithCode()
    {
        var text = Text("""
            import "math"

            // Not a module header.
            func util_abs(value: int) : int { return value }
            """);

        Assert.Null(DocumentationExtractor.ForModule(text));
    }

    /// <summary>
    /// The extractor works against the library it was written for.
    /// </summary>
    /// <remarks>
    /// Every module in <c>std/</c> opens with a block explaining what it is and why it is shaped
    /// the way it is. This asserts that an editor can actually show that, and it is the test that
    /// would fail if someone added a module without documenting it.
    /// </remarks>
    [Fact]
    public void ForModule_ReadsEveryStandardLibraryModule()
    {
        var undocumented = new List<string>();

        foreach (var file in Directory.EnumerateFiles(TestPaths.StandardLibrary, "*.prl", SearchOption.AllDirectories))
        {
            var text = SourceText.From(File.ReadAllText(file), file);

            if (string.IsNullOrWhiteSpace(DocumentationExtractor.ForModule(text)))
            {
                undocumented.Add(Path.GetFileName(file));
            }
        }

        Assert.Empty(undocumented);
    }
}
