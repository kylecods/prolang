using System.Collections.Immutable;

namespace ProLang.Syntax;

public abstract class DeclarationSyntax : SyntaxNode
{
    protected DeclarationSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}

public sealed class StructDeclarationSyntax : DeclarationSyntax
{
    public StructDeclarationSyntax(SyntaxTree syntaxTree, SyntaxToken structKeyword, SyntaxToken identifier, SyntaxToken? lessThanToken, SeparatedSyntaxList<SyntaxToken> typeParameters, SyntaxToken? greaterThanToken, SyntaxToken openCurlyToken, ImmutableArray<FieldDeclarationSyntax> fields, SyntaxToken closeCurlyToken) : base(syntaxTree)
    {
        StructKeyword = structKeyword;
        Identifier = identifier;
        LessThanToken = lessThanToken;
        TypeParameters = typeParameters;
        GreaterThanToken = greaterThanToken;
        OpenCurlyToken = openCurlyToken;
        Fields = fields;
        CloseCurlyToken = closeCurlyToken;
    }

    public SyntaxToken StructKeyword { get; }
    public SyntaxToken Identifier { get; }
    public SyntaxToken? LessThanToken { get; }
    public SeparatedSyntaxList<SyntaxToken> TypeParameters { get; }
    public SyntaxToken? GreaterThanToken { get; }
    public SyntaxToken OpenCurlyToken { get; }
    public ImmutableArray<FieldDeclarationSyntax> Fields { get; }
    public SyntaxToken CloseCurlyToken { get; }

    public override SyntaxKind Kind => SyntaxKind.StructDeclaration;
}