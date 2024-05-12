using System.Collections.Immutable;

namespace ProLang.Syntax;

internal sealed class BlockStatementSyntax : StatementSyntax
{
    public BlockStatementSyntax(SyntaxTree syntaxTree,SyntaxToken leftCurly, ImmutableArray<StatementSyntax> statements, SyntaxToken rightCurly)
    : base(syntaxTree)
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