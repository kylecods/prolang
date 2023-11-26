using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Text;

namespace ProLang.Syntax;

internal sealed class SyntaxTree
{
    public SyntaxTree(SourceText text,ImmutableArray<Diagnostic> diagnostics, SyntaxNode root, SyntaxToken eofToken)
    {
        Text = text;
        Diagnostics = diagnostics;
        Root = root;
        EndOfFileToken = eofToken;
    }
    
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    
    public SourceText Text { get; }
    
    public SyntaxNode Root { get; }
    
    public SyntaxToken EndOfFileToken { get; }

    public static SyntaxTree Parse(SourceText text)
    {
        var parser = new Parser(text);

        return parser.Parse();
    }

    public static SyntaxTree Parse(string text)
    {
        var sourceText = SourceText.From(text);
        return Parse(sourceText);
    }

    public static IEnumerable<SyntaxToken> ParseTokens(string text)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText);
    }

    public static IEnumerable<SyntaxToken> ParseTokens(SourceText text)
    {
        var lexer = new Lexer(text);
        while (true)
        {
            var token = lexer.Lex();
            if (token.Kind == SyntaxKind.EofToken)
            {
                break;
            }

            yield return token;
        }
    }
}