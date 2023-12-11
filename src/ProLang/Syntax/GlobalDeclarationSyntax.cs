using System.Collections.Immutable;

namespace ProLang.Syntax;

internal class GlobalDeclarationSyntax : SyntaxNode
{
    public GlobalDeclarationSyntax(ImmutableArray<DeclarationSyntax> declarations, SyntaxToken eofToken)
    {
        Declarations = declarations;
        EofToken = eofToken;
    }
    
    public ImmutableArray<DeclarationSyntax> Declarations { get; }
    
    public SyntaxToken EofToken { get; }
    
    public override SyntaxKind Kind => SyntaxKind.GlobalDeclaration;
}