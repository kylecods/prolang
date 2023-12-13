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