namespace ProLang.Syntax;

internal sealed class GlobalVariableDeclarationSyntax : DeclarationSyntax
{
    public GlobalVariableDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken globalKeyword, SyntaxToken identifier, TypeClauseSyntax? typeClause, SyntaxToken equalsToken, ExpressionSyntax? expression)
     : base(syntaxTree)
    {
        GlobalKeyword = globalKeyword;
        Identifier = identifier;
        TypeClause = typeClause;
        EqualsToken = equalsToken;
        Expression = expression;
    }

    public override SyntaxKind Kind => SyntaxKind.GlobalVariableDeclaration;
    public SyntaxToken GlobalKeyword { get; }
    public SyntaxToken Identifier { get; }

    public TypeClauseSyntax? TypeClause { get; }
    public SyntaxToken EqualsToken { get; }
    public ExpressionSyntax? Expression { get; }
}
