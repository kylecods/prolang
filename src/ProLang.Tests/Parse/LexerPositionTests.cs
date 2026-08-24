using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Tests.Parse;

/// <summary>
/// Pins the source position carried by every token.
/// </summary>
/// <remarks>
/// <para>
/// The lexer used to construct each token with <c>_position</c> — the offset it had already
/// advanced to — rather than <c>_start</c>. Every token span, and therefore every node span and
/// every diagnostic column in the compiler, sat one token-length to the right of the text it
/// described.
/// </para>
/// <para>
/// Nothing caught it. No snapshot or expected-output file records a diagnostic position, and the
/// renderer in <c>TextWriterExtensions</c> wraps the code that would have thrown on the resulting
/// out-of-range span in a bare <c>catch</c>, so a wrong span printed as no span at all. These
/// tests exist because a language server is built entirely out of these numbers.
/// </para>
/// </remarks>
public class LexerPositionTests
{
    /// <summary>
    /// The strongest statement available: concatenating the token stream reproduces the source
    /// byte for byte, with every token at the offset it claims.
    /// </summary>
    /// <remarks>
    /// This pins two things at once. That positions are correct, and that the stream is lossless —
    /// comments reach the token stream as <see cref="SyntaxKind.WhitespaceToken"/> carrying their
    /// own text, which is what lets a formatter and a documentation extractor work without the
    /// compiler having a trivia model.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("let x = 1")]
    [InlineData("func main() { print(\"hello\") }")]
    [InlineData("// a comment\nfunc main() { }")]
    [InlineData("/* block\n   comment */ let x: int = 0x1F")]
    [InlineData("struct Point { x: int; y: int }")]
    [InlineData("enum Colour { Red, Green = 5 }")]
    [InlineData("let f: func(int) : bool = predicate")]
    [InlineData("for (let i = 0 to 10) { arr[i] = i * 2 }")]
    [InlineData("let s = \"quote \"\" inside\"\nlet n = 1.5f32")]
    [InlineData("import \"ui/shape\"\r\nimport \"io\"\r\n")]
    public void TokenStream_ReconstructsTheSource(string text)
    {
        var sourceText = SourceText.From(text, "test.prl");
        var tokens = SyntaxTree.ParseTokens(sourceText);

        var offset = 0;

        foreach (var token in tokens)
        {
            Assert.Equal(offset, token.Position);
            Assert.Equal(token.Text, sourceText.ToString(token.Span));

            offset += token.Text.Length;
        }

        Assert.Equal(text.Length, offset);
    }

    /// <summary>
    /// A parser diagnostic points at the token the parser actually objected to.
    /// </summary>
    [Fact]
    public void ParserDiagnostic_PointsAtTheOffendingToken()
    {
        var sourceText = SourceText.From("func main() { print(1 }", "test.prl");
        var tree = SyntaxTree.Parse(sourceText);

        var diagnostic = Assert.Single(tree.Diagnostics);

        Assert.Equal("}", sourceText.ToString(diagnostic.Location.Span));
    }

    /// <summary>
    /// Line and character, which is the form the editor consumes, on a token that is not on the
    /// first line — where an off-by-one in the line lookup would otherwise hide.
    /// </summary>
    [Fact]
    public void TokenLocation_ReportsLineAndCharacter()
    {
        var sourceText = SourceText.From("func main() {\n    let value = 1\n}", "test.prl");
        var tokens = SyntaxTree.ParseTokens(sourceText);

        var value = tokens.Single(t => t.Text == "value");

        Assert.Equal(1, value.Location.StartLine);
        Assert.Equal(8, value.Location.StartCharacter);
        Assert.Equal(1, value.Location.EndLine);
        Assert.Equal(13, value.Location.EndCharacter);
    }
}
