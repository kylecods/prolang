using System.Collections.Immutable;

namespace ProLang.Syntax;

internal class GlobalDeclarationSyntax : SyntaxNode
{
    public GlobalDeclarationSyntax(ImmutableArray<DeclarationSyntax> declarations)
    {
        Declarations = declarations;
    }
    
    public ImmutableArray<DeclarationSyntax> Declarations { get; }
    
    public override SyntaxKind Kind => SyntaxKind.GlobalDeclaration;
}