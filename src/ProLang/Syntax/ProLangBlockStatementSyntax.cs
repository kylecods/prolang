using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class ProLangBlockStatementSyntax : StatementSyntax
{
    public ProLangBlockStatementSyntax(SyntaxToken openToken, ImmutableArray<StatementSyntax> statements, SyntaxToken closeToken)
    {
        OpenToken = openToken;
        Statements = statements;
        CloseToken = closeToken;
    }

    public SyntaxToken OpenToken { get; }
    
    public ImmutableArray<StatementSyntax> Statements { get; }
    
    public SyntaxToken CloseToken { get; }
    public override SyntaxKind Kind => SyntaxKind.ProLangBlockStatement;
}