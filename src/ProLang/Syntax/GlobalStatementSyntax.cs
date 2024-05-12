namespace ProLang.Syntax;

internal sealed class GlobalStatementSyntax : DeclarationSyntax
{
    public GlobalStatementSyntax(SyntaxTree syntaxTree,StatementSyntax statement) : base(syntaxTree)
    {
        Statement = statement;
    }

    public StatementSyntax Statement { get; }
    public override SyntaxKind Kind => SyntaxKind.GlobalStatement;
}