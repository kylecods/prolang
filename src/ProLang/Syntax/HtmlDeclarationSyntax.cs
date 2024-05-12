using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class HtmlDeclarationSyntax : DeclarationSyntax
{
    public HtmlDeclarationSyntax(SyntaxTree syntaxTree,ImmutableArray<HtmlStatementSyntax> htmlStatements) : base(syntaxTree)
    {
        HtmlStatements = htmlStatements;
    }

    public override SyntaxKind Kind => SyntaxKind.HtmlDeclaration;
    
    public ImmutableArray<HtmlStatementSyntax> HtmlStatements { get; }
    
    
    
}