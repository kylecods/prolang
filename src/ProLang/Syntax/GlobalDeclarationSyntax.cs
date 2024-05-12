using System.Collections.Immutable;

namespace ProLang.Syntax;

public sealed class GlobalDeclarationSyntax : SyntaxNode
{
    public GlobalDeclarationSyntax(SyntaxTree syntaxTree,ImmutableArray<DeclarationSyntax> declarations, SyntaxToken eofToken) : base(syntaxTree)
    {
        Declarations = declarations;
        EofToken = eofToken;
    }
    
    public ImmutableArray<DeclarationSyntax> Declarations { get; }
    
    public SyntaxToken EofToken { get; }
    
    public override SyntaxKind Kind => SyntaxKind.GlobalDeclaration;
}