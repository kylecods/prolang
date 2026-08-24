using ProLang.Symbols;
using ProLang.Symbols.Modules;
using ProLang.Tests.Infrastructure;
using ProLang.Text;

namespace ProLang.Tests.Documentation;

/// <summary>
/// Every builtin says what it does, and every module says what it is for.
/// </summary>
/// <remarks>
/// A builtin has no source file, so a comment cannot document it and the declaration in
/// <c>BuiltInFunctions</c> is the only place its description can live. That makes it easy to add a
/// builtin and forget — which is what had happened: two of about forty had any description at all.
/// These tests are the reason that cannot quietly happen again.
/// </remarks>
public class BuiltInDocumentationTests
{
    public static TheoryData<string, string> AllBuiltIns()
    {
        var data = new TheoryData<string, string>();

        foreach (var module in BuiltInModule.GetIntrinsic())
        {
            foreach (var function in module.Functions)
            {
                data.Add(module.Name, function.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllBuiltIns))]
    public void EveryBuiltIn_IsDocumented(string moduleName, string functionName)
    {
        Assert.True(BuiltInModule.TryGetModule(moduleName, out var module));

        var function = module!.Functions.First(f => f.Name == functionName);

        Assert.False(string.IsNullOrWhiteSpace(function.Documentation),
            $"Builtin '{functionName}' in module '{moduleName}' has no Documentation. It is what a "
            + "user sees on hover, in the completion list and in signature help, and a builtin has "
            + "no source file for a comment to live in. See docs/contributing/adding-a-builtin.md.");
    }

    [Fact]
    public void EveryModule_HasASummary()
    {
        foreach (var module in BuiltInModule.GetIntrinsic())
        {
            Assert.False(string.IsNullOrWhiteSpace(module.Summary),
                $"Built-in module '{module.Name}' has no Summary, so nothing describes it while "
                + "someone is choosing what to import.");
        }
    }

    /// <summary>
    /// Parameter names are user-visible, so they have to read as names.
    /// </summary>
    /// <remarks>
    /// They are not decoration: they are what a named argument is written with, what signature
    /// help labels, and what an inlay hint shows at a call site. <c>min(arg1, arg2)</c> told the
    /// reader nothing, which is why it is now <c>min(a, b)</c>.
    /// </remarks>
    [Fact]
    public void NoBuiltIn_HasAPlaceholderParameterName()
    {
        var offenders = new List<string>();

        foreach (var module in BuiltInModule.GetIntrinsic())
        {
            foreach (var function in module.Functions)
            {
                foreach (var parameter in function.Parameters)
                {
                    if (parameter.Name.StartsWith("arg", StringComparison.OrdinalIgnoreCase)
                        || parameter.Name.Length == 0)
                    {
                        offenders.Add($"{function.Name}({parameter.Name})");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Documentation reaches an editor through the symbol, whichever kind of symbol it is.
    /// </summary>
    /// <remarks>
    /// The point of hanging it on <see cref="Symbol"/> rather than on the builtin table: hover
    /// reads one property and never asks where the symbol came from.
    /// </remarks>
    [Fact]
    public void DeclaredFunctions_CarryTheCommentAboveThem()
    {
        var source = MarkedSource.Parse("""
            // Absolute value. ProLang's unary minus is defined for `int` only.
            func util_abs(value: int) : int {
                if (value < 0) { return 0 - value }
                return value
            }

            func main() {
                let x: int = util_|abs(0 - 5)
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal(
            "Absolute value. ProLang's unary minus is defined for `int` only.",
            symbol?.Documentation);
    }

    [Fact]
    public void DeclaredStructs_CarryTheCommentAboveThem()
    {
        var source = MarkedSource.Parse("""
            /*
             * A pixel buffer: flat row-major ARGB plus its size.
             */
            struct Document { width: int; height: int }

            func main() {
                let d: Docu|ment = Document { width: 1, height: 1 }
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("A pixel buffer: flat row-major ARGB plus its size.", symbol?.Documentation);
    }

    /// <summary>
    /// The standard library is documented, from the comments it already had.
    /// </summary>
    /// <remarks>
    /// This is the requirement stated as a test: hovering a std function in an editor shows the
    /// prose written above it in <c>std/</c>. It binds the real library rather than a fixture, so
    /// it fails if either the extractor or the library's comment style drifts.
    /// </remarks>
    [Fact]
    public void StandardLibraryFunctions_AreDocumentedFromTheirSource()
    {
        var source = MarkedSource.Parse("""
            import "util"

            func main() {
                let clamped: int = util_|clamp(5, 0, 3)
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("util_clamp", symbol?.Name);
        Assert.Contains("inclusive range", symbol?.Documentation);
    }

    [Fact]
    public void BuiltInFunctions_AreDocumentedThroughTheSameProperty()
    {
        var source = MarkedSource.Parse("""
            import "console"

            func main() {
                let start: int = time_mi|llis()
            }
            """);

        var symbol = source.Bind().SymbolAt(source.FileName, source.Position);

        Assert.Equal("time_millis", symbol?.Name);
        Assert.Contains("monotonic", symbol?.Documentation);
    }
}
