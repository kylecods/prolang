using System.Collections.Immutable;

namespace ProLang.Syntax;

public abstract class DeclarationSyntax : SyntaxNode
{
    protected DeclarationSyntax(SyntaxTree syntaxTree) : base(syntaxTree)
    {
    }
}

/// <summary>
/// <c>imp Rect { func ... }</c> — functions associated with a type.
/// </summary>
/// <remarks>
/// <para>
/// Members are ordinary <see cref="FunctionDeclarationSyntax"/> nodes produced by the same parse
/// method as a top-level <c>func</c>. That is deliberate and load-bearing: it means an imp member has
/// the same declaration type as any other function, so <c>FunctionSymbol.Declaration</c> keeps
/// working and the binder's body-binding pass needs no change at all.
/// </para>
/// <para>
/// Properties are declared in source order because <c>SyntaxNode.GetChildren</c> is reflection-based
/// and the span is computed from the first and last child. <see cref="TypeParameters"/> is always a
/// list, never null — that branch of <c>GetChildren</c> is unguarded.
/// </para>
/// </remarks>
public sealed class ImpDeclarationSyntax : DeclarationSyntax
{
    public ImpDeclarationSyntax(
        SyntaxTree syntaxTree,
        SyntaxToken impKeyword,
        SyntaxToken identifier,
        SyntaxToken? lessThanToken,
        SeparatedSyntaxList<SyntaxToken> typeParameters,
        SyntaxToken? greaterThanToken,
        SyntaxToken openCurlyToken,
        ImmutableArray<FunctionDeclarationSyntax> functions,
        SyntaxToken closeCurlyToken) : base(syntaxTree)
    {
        ImpKeyword = impKeyword;
        Identifier = identifier;
        LessThanToken = lessThanToken;
        TypeParameters = typeParameters;
        GreaterThanToken = greaterThanToken;
        OpenCurlyToken = openCurlyToken;
        Functions = functions;
        CloseCurlyToken = closeCurlyToken;
    }

    public SyntaxToken ImpKeyword { get; }

    /// <summary>The type these functions belong to.</summary>
    public SyntaxToken Identifier { get; }

    public SyntaxToken? LessThanToken { get; }
    public SeparatedSyntaxList<SyntaxToken> TypeParameters { get; }
    public SyntaxToken? GreaterThanToken { get; }
    public SyntaxToken OpenCurlyToken { get; }
    public ImmutableArray<FunctionDeclarationSyntax> Functions { get; }
    public SyntaxToken CloseCurlyToken { get; }

    public override SyntaxKind Kind => SyntaxKind.ImpDeclaration;
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