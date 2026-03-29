using System.Collections.Immutable;

namespace ProLang.Syntax;

public abstract class StatementSyntax : SyntaxNode
{
    protected StatementSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}

public sealed class FieldDeclarationSyntax : SyntaxNode
{
    public FieldDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken identifier, TypeClauseSyntax type) : base(syntaxTree)
    {
        Identifier = identifier;
        Type = type;
    }

    public SyntaxToken Identifier { get; }
    public TypeClauseSyntax Type { get; }

    public override SyntaxKind Kind => SyntaxKind.FieldDeclaration;
}