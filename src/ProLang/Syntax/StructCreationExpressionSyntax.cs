using System.Collections.Immutable;
using ProLang.Syntax;

namespace ProLang.Syntax;

internal sealed class StructCreationExpressionSyntax : ExpressionSyntax
{
    public StructCreationExpressionSyntax(SyntaxTree syntaxTree, SyntaxToken typeName, SyntaxToken openCurlyToken, ImmutableArray<FieldInitializerSyntax> initializers, SyntaxToken closeCurlyToken) : base(syntaxTree)
    {
        TypeName = typeName;
        OpenCurlyToken = openCurlyToken;
        Initializers = initializers;
        CloseCurlyToken = closeCurlyToken;
    }

    public SyntaxToken TypeName { get; }
    public SyntaxToken OpenCurlyToken { get; }
    public ImmutableArray<FieldInitializerSyntax> Initializers { get; }
    public SyntaxToken CloseCurlyToken { get; }

    public override SyntaxKind Kind => SyntaxKind.StructCreationExpression;
}

internal sealed class FieldInitializerSyntax : SyntaxNode
{
    public FieldInitializerSyntax(SyntaxTree syntaxTree, SyntaxToken fieldName, SyntaxToken colonToken, ExpressionSyntax expression) : base(syntaxTree)
    {
        FieldName = fieldName;
        ColonToken = colonToken;
        Expression = expression;
    }

    public SyntaxToken FieldName { get; }
    public SyntaxToken ColonToken { get; }
    public ExpressionSyntax Expression { get; }

    public override SyntaxKind Kind => SyntaxKind.FieldInitializer;
}