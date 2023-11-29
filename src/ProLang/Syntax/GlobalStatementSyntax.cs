namespace ProLang.Syntax;

internal sealed class GlobalStatementSyntax : DeclarationSyntax
{
    public GlobalStatementSyntax(StatementSyntax statement)
    {
        Statement = statement;
    }

    public StatementSyntax Statement { get; }
    public override SyntaxKind Kind => SyntaxKind.GlobalStatement;
}