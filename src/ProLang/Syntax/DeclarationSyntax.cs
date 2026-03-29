using System.Collections.Immutable;

namespace ProLang.Syntax;

public abstract class DeclarationSyntax : SyntaxNode
{
    protected DeclarationSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}

public sealed class StructDeclarationSyntax : DeclarationSyntax
{
    public StructDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken structKeyword, SyntaxToken identifier, SyntaxToken openCurlyToken, ImmutableArray<FieldDeclarationSyntax> fields, SyntaxToken closeCurlyToken) : base(syntaxTree)
    {
        StructKeyword = structKeyword;
        Identifier = identifier;
        OpenCurlyToken = openCurlyToken;
        Fields = fields;
        CloseCurlyToken = closeCurlyToken;
    }

    public SyntaxToken StructKeyword { get; }
    public SyntaxToken Identifier { get; }
    public SyntaxToken OpenCurlyToken { get; }
    public ImmutableArray<FieldDeclarationSyntax> Fields { get; }
    public SyntaxToken CloseCurlyToken { get; }

    public override SyntaxKind Kind => SyntaxKind.StructDeclaration;
}