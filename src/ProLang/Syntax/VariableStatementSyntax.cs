namespace ProLang.Syntax;

internal sealed class VariableStatementSyntax : StatementSyntax
{
    public VariableStatementSyntax(SyntaxTree syntaxTree,SyntaxToken keyword, SyntaxToken identifier, TypeClauseSyntax? typeClause, SyntaxToken equalsToken, ExpressionSyntax expression)
     :base(syntaxTree)
    {
        Keyword = keyword;
        Identifier = identifier;
        TypeClause = typeClause;
        EqualsToken = equalsToken;
        Expression = expression;
    }
    public override SyntaxKind Kind => SyntaxKind.VariableDeclaration;
    public SyntaxToken Keyword { get; }
    public SyntaxToken Identifier { get; }
    
    public TypeClauseSyntax? TypeClause { get; }
    public SyntaxToken EqualsToken { get; }
    public ExpressionSyntax Expression { get; }

}