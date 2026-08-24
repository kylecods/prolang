using ProLang.Lsp.Workspace;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Lsp.Handlers;

/// <summary>What kind of thing belongs at the cursor.</summary>
internal enum CompletionKind
{
    /// <summary>Anything nameable: locals, functions, types, keywords.</summary>
    Expression,

    /// <summary>After a <c>.</c> — only what the thing on the left has.</summary>
    Member,

    /// <summary>After <c>:</c> in a declaration, after <c>as</c>, or inside <c>&lt; &gt;</c>.</summary>
    Type,

    /// <summary>Inside the string of an <c>import</c>.</summary>
    ImportPath,

    /// <summary>Inside a comment or a string — nothing to offer.</summary>
    None,
}

/// <summary>
/// Works out what the cursor is in the middle of.
/// </summary>
/// <remarks>
/// <para>
/// Determined from the token stream rather than the syntax tree, because completion is asked for
/// on text that is mid-edit and therefore usually does not parse: <c>doc.</c> is not a valid field
/// access, and <c>let x: </c> is not a valid declaration. Tokens survive that; a tree does not.
/// </para>
/// <para>
/// This is what the old server was missing entirely. It declared <c>.</c>, <c>"</c>, <c>(</c>,
/// <c>,</c> and <c>:</c> as trigger characters, then returned the same list of every keyword and
/// every symbol in the file regardless of which one had fired — including inside strings and
/// comments.
/// </para>
/// </remarks>
internal sealed class CompletionContext
{
    private CompletionContext(CompletionKind kind)
    {
        Kind = kind;
    }

    public CompletionKind Kind { get; private init; }

    /// <summary>What is typed so far at the cursor, for the editor to filter on.</summary>
    public string Prefix { get; private init; } = string.Empty;

    /// <summary>For <see cref="CompletionKind.Member"/>: the span of the expression before the dot.</summary>
    public TextSpan ReceiverSpan { get; private init; }

    /// <summary>For <see cref="CompletionKind.Member"/>: the bare name before the dot, if it is one.</summary>
    public string? ReceiverName { get; private init; }

    /// <summary>For <see cref="CompletionKind.ImportPath"/>: the text already inside the quotes.</summary>
    public string ImportPrefix { get; private init; } = string.Empty;

    /// <summary>The call the cursor is inside, for offering parameter names.</summary>
    public string? EnclosingCallName { get; private init; }

    public static CompletionContext Determine(SourceText text, int position)
    {
        var tokens = SyntaxTree.ParseTokens(text);

        // Everything before the cursor, ignoring whitespace and comments — which the lexer folds
        // together into one kind, and whose text is the comment itself.
        var before = new List<SyntaxToken>();
        SyntaxToken? containing = null;

        foreach (var token in tokens)
        {
            if (token.Position >= position)
            {
                break;
            }

            if (token.Position + (token.Text?.Length ?? 0) >= position)
            {
                containing = token;
            }

            if (token.Kind != SyntaxKind.WhitespaceToken)
            {
                before.Add(token);
            }
        }

        if (containing is { Kind: SyntaxKind.WhitespaceToken } comment && IsComment(comment))
        {
            return new CompletionContext(CompletionKind.None);
        }

        var importPrefix = ImportPrefixAt(text, tokens, position);

        if (importPrefix != null)
        {
            return new CompletionContext(CompletionKind.ImportPath) { ImportPrefix = importPrefix };
        }

        if (containing is { Kind: SyntaxKind.StringToken })
        {
            return new CompletionContext(CompletionKind.None);
        }

        // A partly typed word is a token in its own right; the cursor sits at its end.
        var prefix = string.Empty;
        var index = before.Count - 1;

        if (index >= 0 && before[index].Kind == SyntaxKind.IdentifierToken
            && before[index].Position + before[index].Text.Length == position)
        {
            prefix = before[index].Text;
            index--;
        }

        var enclosingCall = FindEnclosingCall(before, index);

        if (index >= 0 && before[index].Kind == SyntaxKind.DotToken)
        {
            var (span, name) = ReceiverBefore(before, index - 1);

            return new CompletionContext(CompletionKind.Member)
            {
                Prefix = prefix,
                ReceiverSpan = span,
                ReceiverName = name,
                EnclosingCallName = enclosingCall,
            };
        }

        if (index >= 0 && IsTypePosition(before, index))
        {
            return new CompletionContext(CompletionKind.Type) { Prefix = prefix };
        }

        return new CompletionContext(CompletionKind.Expression)
        {
            Prefix = prefix,
            EnclosingCallName = enclosingCall,
        };
    }

    private static bool IsComment(SyntaxToken token) =>
        token.Text != null && (token.Text.Contains("//") || token.Text.Contains("/*"));

    /// <summary>
    /// A colon introduces a type — except in a map literal, a struct initialiser or a named
    /// argument, where it introduces a value.
    /// </summary>
    private static bool IsTypePosition(IReadOnlyList<SyntaxToken> before, int index)
    {
        var token = before[index];

        if (token.Kind == SyntaxKind.AsKeyword)
        {
            return true;
        }

        if (token.Kind != SyntaxKind.ColonToken)
        {
            return false;
        }

        // `let x: T`, `field: T` in a struct declaration, and `p: T` in a parameter list all have
        // an identifier before the colon; so does a named argument. What separates them is what
        // comes before *that*.
        if (index < 2)
        {
            return false;
        }

        var beforeName = before[index - 2].Kind;

        return beforeName is SyntaxKind.LetKeyword
            or SyntaxKind.LeftParenthesisToken
            or SyntaxKind.CommaToken
            or SyntaxKind.LeftCurlyToken
            or SyntaxKind.SemiColonToken
            or SyntaxKind.RightParenthesisToken;
    }

    /// <summary>The expression immediately left of a dot.</summary>
    private static (TextSpan Span, string? Name) ReceiverBefore(IReadOnlyList<SyntaxToken> before, int index)
    {
        if (index < 0)
        {
            return (default, null);
        }

        var end = before[index].Position + (before[index].Text?.Length ?? 0);
        var start = before[index].Position;
        string? name = before[index].Kind == SyntaxKind.IdentifierToken ? before[index].Text : null;

        // Walk back over a balanced call or index so that `getBox(i).` and `arr[0].` are answered
        // by the type of the whole expression rather than of the bracket.
        var depth = 0;

        for (var i = index; i >= 0; i--)
        {
            var kind = before[i].Kind;

            if (kind is SyntaxKind.RightParenthesisToken or SyntaxKind.RightBracketToken)
            {
                depth++;
            }
            else if (kind is SyntaxKind.LeftParenthesisToken or SyntaxKind.LeftBracketToken)
            {
                depth--;

                if (depth < 0)
                {
                    break;
                }
            }
            else if (depth == 0 && kind is not (SyntaxKind.IdentifierToken or SyntaxKind.DotToken))
            {
                break;
            }

            start = before[i].Position;

            if (depth == 0 && i > 0 && before[i - 1].Kind is not (SyntaxKind.IdentifierToken or SyntaxKind.DotToken
                or SyntaxKind.RightParenthesisToken or SyntaxKind.RightBracketToken))
            {
                break;
            }
        }

        return (TextSpan.FromBounds(start, end), name);
    }

    /// <summary>The name of the call the cursor is inside, if it is inside one.</summary>
    private static string? FindEnclosingCall(IReadOnlyList<SyntaxToken> before, int index)
    {
        var depth = 0;

        for (var i = index; i >= 0; i--)
        {
            var kind = before[i].Kind;

            if (kind == SyntaxKind.RightParenthesisToken)
            {
                depth++;
            }
            else if (kind == SyntaxKind.LeftParenthesisToken)
            {
                if (depth == 0)
                {
                    return i > 0 && before[i - 1].Kind == SyntaxKind.IdentifierToken
                        ? before[i - 1].Text
                        : null;
                }

                depth--;
            }
            else if (kind is SyntaxKind.LeftCurlyToken or SyntaxKind.RightCurlyToken)
            {
                // A brace ends the search: a call's arguments never span a block.
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The text so far inside <c>import "…</c>, or null if the cursor is not in one.
    /// </summary>
    /// <remarks>
    /// Read from the raw text rather than from tokens because an unterminated string is what is
    /// being typed, and the lexer reports that as an error rather than as a usable token.
    /// </remarks>
    private static string? ImportPrefixAt(SourceText text, IEnumerable<SyntaxToken> tokens, int position)
    {
        var line = text.Lines[text.GetLineIndex(position)];
        var upToCursor = text.ToString(TextSpan.FromBounds(line.Start, position));
        var trimmed = upToCursor.TrimStart();

        if (!trimmed.StartsWith("import", StringComparison.Ordinal))
        {
            return null;
        }

        var quote = upToCursor.IndexOf('"');

        if (quote < 0)
        {
            return null;
        }

        // A second quote before the cursor means the string is closed and the cursor is past it.
        var closing = upToCursor.IndexOf('"', quote + 1);

        return closing >= 0 ? null : upToCursor[(quote + 1)..];
    }
}
