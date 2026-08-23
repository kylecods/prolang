using System.Collections.Immutable;

namespace ProLang.Syntax;

/// <summary>
/// A function type written in a type position: <c>func(int, int) : bool</c>.
/// </summary>
/// <remarks>
/// The return clause is optional and defaults to <c>void</c>, so <c>func(int)</c> is the type of a
/// handler that returns nothing — the common case for a callback.
/// </remarks>
public sealed class FunctionTypeSyntax : TypeSyntax
{
    public FunctionTypeSyntax(SyntaxTree syntaxTree, SyntaxToken functionKeyword,
        SyntaxToken openParenthesisToken, ImmutableArray<TypeSyntax> parameterTypes,
        SyntaxToken closeParenthesisToken, TypeClauseSyntax? returnType) : base(syntaxTree)
    {
        FunctionKeyword = functionKeyword;
        OpenParenthesisToken = openParenthesisToken;
        ParameterTypes = parameterTypes;
        CloseParenthesisToken = closeParenthesisToken;
        ReturnType = returnType;
    }

    public override SyntaxKind Kind => SyntaxKind.FunctionType;

    public SyntaxToken FunctionKeyword { get; }

    public SyntaxToken OpenParenthesisToken { get; }

    public ImmutableArray<TypeSyntax> ParameterTypes { get; }

    public SyntaxToken CloseParenthesisToken { get; }

    /// <summary>The <c>: T</c> return clause, or <see langword="null"/> for a void function type.</summary>
    public TypeClauseSyntax? ReturnType { get; }
}
