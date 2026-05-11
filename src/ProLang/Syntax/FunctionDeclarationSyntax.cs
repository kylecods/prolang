using System.Collections.Immutable;

namespace ProLang.Syntax;

public sealed class FunctionDeclarationSyntax : DeclarationSyntax
{
    public FunctionDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken functionKeyword, SyntaxToken identifier,
        SyntaxToken? lessThanToken, SeparatedSyntaxList<SyntaxToken> typeParameters, SyntaxToken? greaterThanToken,
        SyntaxToken openParenthesisToken, SeparatedSyntaxList<ParameterSyntax> parameters,
        SyntaxToken closeParenthesisToken, TypeClauseSyntax type, BlockStatementSyntax body)
        : base(syntaxTree)
    {
        FunctionKeyword = functionKeyword;
        Identifier = identifier;
        LessThanToken = lessThanToken;
        TypeParameters = typeParameters;
        GreaterThanToken = greaterThanToken;
        OpenParenthesisToken = openParenthesisToken;
        Parameters = parameters;
        CloseParenthesisToken = closeParenthesisToken;
        Type = type;
        Body = body;
    }

    public override SyntaxKind Kind => SyntaxKind.FunctionDeclaration;
    public SyntaxToken FunctionKeyword { get; }
    public SyntaxToken Identifier { get; }
    public SyntaxToken? LessThanToken { get; }
    public SeparatedSyntaxList<SyntaxToken> TypeParameters { get; }
    public SyntaxToken? GreaterThanToken { get; }
    public SyntaxToken OpenParenthesisToken { get; }
    public SeparatedSyntaxList<ParameterSyntax> Parameters { get; }
    public SyntaxToken CloseParenthesisToken { get; }
    public TypeClauseSyntax Type { get; }
    public BlockStatementSyntax Body { get; }
}
