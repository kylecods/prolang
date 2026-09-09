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

    /// <summary>Either <c>struct</c> or <c>class</c>.</summary>
    public SyntaxToken StructKeyword { get; }

    /// <summary>
    /// Whether this declaration was written with <c>class</c>, making it a heap-allocated reference
    /// type rather than a copied value.
    /// </summary>
    /// <remarks>
    /// The two share a syntax node because they differ only in that keyword: same fields, same
    /// generics, same creation expression. A separate node would have forced every consumer that
    /// matches <see cref="StructDeclarationSyntax"/> — the binder's declaration pass, the document
    /// symbol handler, semantic tokens — to grow a parallel case for no gain.
    /// </remarks>
    public bool IsReferenceType => StructKeyword.Kind == SyntaxKind.ClassKeyword;

    public SyntaxToken Identifier { get; }
    public SyntaxToken? LessThanToken { get; }
    public SeparatedSyntaxList<SyntaxToken> TypeParameters { get; }
    public SyntaxToken? GreaterThanToken { get; }
    public SyntaxToken OpenCurlyToken { get; }
    public ImmutableArray<FieldDeclarationSyntax> Fields { get; }
    public SyntaxToken CloseCurlyToken { get; }

    public override SyntaxKind Kind => SyntaxKind.StructDeclaration;
}