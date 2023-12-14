using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class BlockStatementSyntax : StatementSyntax
{
    public BlockStatementSyntax(SyntaxToken leftCurly, ImmutableArray<StatementSyntax> statements, SyntaxToken rightCurly)
    {
        LeftCurly = leftCurly;
        Statements = statements;
        RightCurly = rightCurly;
    }


    public override SyntaxKind Kind => SyntaxKind.BlockStatement;
    
    public SyntaxToken LeftCurly { get; }
    
    public ImmutableArray<StatementSyntax> Statements { get; }
    
    public SyntaxToken RightCurly { get; }
}