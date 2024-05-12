using System.Collections.Immutable;
using ProLang.Parse;
using ProLang.Text;

namespace ProLang.Syntax;

public sealed class SyntaxTree
{
    private delegate void ParserHandler(SyntaxTree syntaxTree, out GlobalDeclarationSyntax root,
        out ImmutableArray<Diagnostic> diagnostics);
    private SyntaxTree(SourceText text, ParserHandler handler)
    {
        Text = text;
        handler(this, out var root, out var diagnostics);
        Diagnostics = diagnostics;
        Root = root;

    }
    
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    
    public SourceText Text { get; }
    
    public GlobalDeclarationSyntax Root { get; }

    public static SyntaxTree Load(string fileName)
    {
        var text = File.ReadAllText(fileName);
        var sourceText = SourceText.From(text, fileName);
        return Parse(sourceText);
    }

    private static void Parse(SyntaxTree syntaxTree, out GlobalDeclarationSyntax root,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var parser = new Parser(syntaxTree);
        root = parser.ParseGlobalDeclaration();
        diagnostics = parser.Diagnostics.ToImmutableArray();
    }
    
    public static SyntaxTree Parse(SourceText text)
    {
        return new SyntaxTree(text,Parse);
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
        var tokens = new List<SyntaxToken>();

        void ParseTokens(SyntaxTree st,out GlobalDeclarationSyntax root,out ImmutableArray<Diagnostic> d)
        {
            root = null!;
            var l = new Lexer(st);
            while (true)
            {
                var token = l.Lex();
                if (token.Kind == SyntaxKind.EofToken)
                {
                    root = new GlobalDeclarationSyntax(st, ImmutableArray<DeclarationSyntax>.Empty, token);
                    break;
                }

                tokens.Add(token);
            }

            d = l.Diagnostics.ToImmutableArray();
        }

        var syntaxTree = new SyntaxTree(text, ParseTokens);
        diagnostics = syntaxTree.Diagnostics.ToImmutableArray();

        return tokens.ToImmutableArray();
    }
}