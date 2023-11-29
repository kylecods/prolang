using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class HtmlDeclarationSyntax : DeclarationSyntax
{
    public HtmlDeclarationSyntax(ImmutableArray<HtmlStatementSyntax> htmlStatements)
    {
        HtmlStatements = htmlStatements;
    }

    public override SyntaxKind Kind => SyntaxKind.HtmlDeclaration;
    
    public ImmutableArray<HtmlStatementSyntax> HtmlStatements { get; }
    
    
    
}