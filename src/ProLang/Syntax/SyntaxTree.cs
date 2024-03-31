using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Text;

namespace ProLang.Syntax;

public sealed class SyntaxTree
{
    private SyntaxTree(SourceText text)
    {
        var parser = new Parser(text);
        var root = parser.ParseGlobalDeclaration();
        
        Text = text;
        Diagnostics = parser.Diagnostics.ToImmutableArray();;
        Root = root;
    }
    
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    
    public SourceText Text { get; }
    
    public GlobalDeclarationSyntax Root { get; }
    
    public static SyntaxTree Parse(SourceText text)
    {
        return new SyntaxTree(text);
    }

    public static SyntaxTree Parse(string text)
    {
        var sourceText = SourceText.From(text);
        return Parse(sourceText);
    }

    public static ImmutableArray<SyntaxToken> ParseTokens(string text)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText);
    }
    
    public static ImmutableArray<SyntaxToken> ParseTokens(string text, out ImmutableArray<Diagnostic> diagnostics)
    {
        var sourceText = SourceText.From(text);
        return ParseTokens(sourceText, out diagnostics);
    }
    
    public static ImmutableArray<SyntaxToken> ParseTokens(SourceText text)
    {
        return ParseTokens(text, out _);
    }


    public static ImmutableArray<SyntaxToken> ParseTokens(SourceText text, out ImmutableArray<Diagnostic> diagnostics)
    {
        IEnumerable<SyntaxToken> LexTokens(Lexer lexer)
        {
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

        var l = new Lexer(text);
        var result = LexTokens(l).ToImmutableArray();
        diagnostics = l.Diagnostics.ToImmutableArray();

        return result;
    }
}