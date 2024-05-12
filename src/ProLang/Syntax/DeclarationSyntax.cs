namespace ProLang.Syntax;

public abstract class DeclarationSyntax : SyntaxNode
{
    protected DeclarationSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}