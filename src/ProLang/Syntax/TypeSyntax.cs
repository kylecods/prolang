namespace ProLang.Syntax;

public abstract class TypeSyntax : SyntaxNode
{
    protected TypeSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}

public sealed class ArrayTypeSyntax : TypeSyntax
{
    public ArrayTypeSyntax(SyntaxTree syntaxTree, TypeSyntax elementType, SyntaxToken openBracket, SyntaxToken closeBracket) : base(syntaxTree)
    {
        ElementType = elementType;
        OpenBracket = openBracket;
        CloseBracket = closeBracket;
    }

    public override SyntaxKind Kind => SyntaxKind.ArrayType;
    public TypeSyntax ElementType { get; }
    public SyntaxToken OpenBracket { get; }
    public SyntaxToken CloseBracket { get; }
}
