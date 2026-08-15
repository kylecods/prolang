using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class EnumMemberSyntax : SyntaxNode
{
    public EnumMemberSyntax(SyntaxTree syntaxTree, SyntaxToken identifier, SyntaxToken? equalsToken, ExpressionSyntax? initializer) : base(syntaxTree)
    {
        Identifier = identifier;
        EqualsToken = equalsToken;
        Initializer = initializer;
    }

    public SyntaxToken Identifier { get; }
    public SyntaxToken? EqualsToken { get; }
    public ExpressionSyntax? Initializer { get; }

    public override SyntaxKind Kind => SyntaxKind.EnumMemberDeclaration;
}

internal sealed class EnumDeclarationSyntax : DeclarationSyntax
{
    public EnumDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken enumKeyword, SyntaxToken identifier, SyntaxToken openCurlyToken, ImmutableArray<EnumMemberSyntax> members, SyntaxToken closeCurlyToken) : base(syntaxTree)
    {
        EnumKeyword = enumKeyword;
        Identifier = identifier;
        OpenCurlyToken = openCurlyToken;
        Members = members;
        CloseCurlyToken = closeCurlyToken;
    }

    public SyntaxToken EnumKeyword { get; }
    public SyntaxToken Identifier { get; }
    public SyntaxToken OpenCurlyToken { get; }
    public ImmutableArray<EnumMemberSyntax> Members { get; }
    public SyntaxToken CloseCurlyToken { get; }

    public override SyntaxKind Kind => SyntaxKind.EnumDeclaration;
}
