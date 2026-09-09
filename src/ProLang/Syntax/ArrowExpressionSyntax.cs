using System.Collections.Immutable;

namespace ProLang.Syntax;

/// <summary>
/// A call through <c>-&gt;</c>: <c>Rect-&gt;new()</c> or <c>rect-&gt;area()</c>.
/// </summary>
/// <remarks>
/// The receiver is an arbitrary expression rather than a token, because the same syntax covers both
/// a type name and a value. Which one it is cannot be decided while parsing — <c>Rect</c> and
/// <c>rect</c> are the same shape — so the binder decides, using the rule <c>.</c> already uses for
/// <c>StringBuilder.new()</c>: a bare name that is not a declared variable is a type name.
/// <para>
/// Unlike <see cref="MethodCallExpressionSyntax"/> this carries explicit type arguments, so that
/// <c>arr-&gt;get&lt;int&gt;(0)</c> can be written. That matters because generic inference cannot
/// currently see through a struct instantiation, which is why every <c>DynArray</c> call site in the
/// repository spells its type arguments out.
/// </para>
/// </remarks>
internal sealed class ArrowCallExpressionSyntax : ExpressionSyntax
{
    public ArrowCallExpressionSyntax(
        SyntaxTree syntaxTree,
        ExpressionSyntax expression,
        SyntaxToken arrowToken,
        SyntaxToken name,
        SyntaxToken? lessThanToken,
        ImmutableArray<TypeSyntax> typeArguments,
        SyntaxToken? greaterThanToken,
        SyntaxToken openParenthesisToken,
        SeparatedSyntaxList<ExpressionSyntax> arguments,
        SyntaxToken closeParenthesisToken)
        : base(syntaxTree)
    {
        Expression = expression;
        ArrowToken = arrowToken;
        Name = name;
        LessThanToken = lessThanToken;
        TypeArguments = typeArguments;
        GreaterThanToken = greaterThanToken;
        OpenParenthesisToken = openParenthesisToken;
        Arguments = arguments;
        CloseParenthesisToken = closeParenthesisToken;
    }

    public override SyntaxKind Kind => SyntaxKind.ArrowCallExpression;

    public ExpressionSyntax Expression { get; }
    public SyntaxToken ArrowToken { get; }
    public SyntaxToken Name { get; }
    public SyntaxToken? LessThanToken { get; }
    public ImmutableArray<TypeSyntax> TypeArguments { get; }
    public SyntaxToken? GreaterThanToken { get; }
    public SyntaxToken OpenParenthesisToken { get; }
    public SeparatedSyntaxList<ExpressionSyntax> Arguments { get; }
    public SyntaxToken CloseParenthesisToken { get; }
}

/// <summary>
/// <c>-&gt;</c> with no argument list: <c>Rect-&gt;area</c>, which is a function value.
/// </summary>
/// <remarks>
/// A separate node rather than an <see cref="ArrowCallExpressionSyntax"/> with a null argument list,
/// mirroring how <c>FieldAccessExpressionSyntax</c> is separate from
/// <see cref="MethodCallExpressionSyntax"/>. It also avoids a real hazard: the reflection-based
/// <c>SyntaxNode.GetChildren</c> does not null-check its <c>SeparatedSyntaxList</c> branch, so a node
/// with a nullable list property would throw while computing its own span.
/// <para>
/// Only a <em>type</em> may appear on the left. A value would produce a bound method, and there is no
/// closure machinery to carry the receiver — a ProLang function value is a bare pointer with a null
/// delegate target.
/// </para>
/// </remarks>
internal sealed class ArrowAccessExpressionSyntax : ExpressionSyntax
{
    public ArrowAccessExpressionSyntax(
        SyntaxTree syntaxTree,
        ExpressionSyntax expression,
        SyntaxToken arrowToken,
        SyntaxToken name)
        : base(syntaxTree)
    {
        Expression = expression;
        ArrowToken = arrowToken;
        Name = name;
    }

    public override SyntaxKind Kind => SyntaxKind.ArrowAccessExpression;

    public ExpressionSyntax Expression { get; }
    public SyntaxToken ArrowToken { get; }
    public SyntaxToken Name { get; }
}
