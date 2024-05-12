namespace ProLang.Syntax;

internal abstract class StatementSyntax : SyntaxNode
{
    protected StatementSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}