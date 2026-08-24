using ProLang.Lsp.Protocol;
using ProLang.Lsp.Workspace;
using ProLang.Symbols;
using ProLang.Syntax;

namespace ProLang.Lsp.Handlers;

/// <summary>
/// The parameter popup shown while typing a call.
/// </summary>
/// <remarks>
/// The two things it has to get right are which function is being called and which argument the
/// cursor is on. The old server got neither: it scanned the whole document for the last <c>name(</c>
/// before the cursor — so a nested call reported the outer function — resolved only three
/// hard-coded builtins, and reported the active parameter as 0 always, so the highlight never
/// moved off the first one.
/// </remarks>
internal static class SignatureHelpHandler
{
    public static SignatureHelp? Help(Analysis analysis, Protocol.Position position)
    {
        var offset = LspConversions.ToOffset(analysis.Text, position);
        var call = FindCall(analysis, offset);

        if (call == null || analysis.Compilation == null)
        {
            return null;
        }

        var function = analysis.Compilation.GetSymbols()
            .OfType<FunctionSymbol>()
            .FirstOrDefault(f => f.Name == call.Value.Name);

        if (function == null)
        {
            return null;
        }

        var parameters = function.Parameters.Select(p => new ParameterInformation
        {
            Label = Describe(p),
        }).ToList();

        return new SignatureHelp
        {
            Signatures =
            [
                new SignatureInformation
                {
                    Label = LspConversions.Signature(function),
                    Documentation = string.IsNullOrWhiteSpace(function.Documentation)
                        ? null
                        : new MarkupContent { Value = function.Documentation },
                    Parameters = parameters,
                },
            ],
            ActiveSignature = 0,
            ActiveParameter = Math.Clamp(call.Value.ActiveParameter, 0, Math.Max(0, parameters.Count - 1)),
        };
    }

    private static string Describe(ParameterSymbol parameter)
    {
        var label = $"{parameter.Name}: {parameter.Type}";

        return parameter.IsOptional ? $"{label} = {parameter.DefaultValue}" : label;
    }

    /// <summary>
    /// The innermost call the cursor is inside, and which argument it is on.
    /// </summary>
    /// <remarks>
    /// Walks the token stream backwards keeping a bracket depth, so commas belonging to a nested
    /// call, an array literal or a generic argument list are not counted as this call's. A named
    /// argument overrides the count outright — once <c>pad:</c> is written, the cursor is on
    /// <c>pad</c> whatever position it occupies.
    /// </remarks>
    private static (string Name, int ActiveParameter)? FindCall(Analysis analysis, int offset)
    {
        var tokens = SyntaxTree.ParseTokens(analysis.Text)
            .Where(t => t.Kind != SyntaxKind.WhitespaceToken && t.Position < offset)
            .ToList();

        var depth = 0;
        var commas = 0;

        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var kind = tokens[i].Kind;

            switch (kind)
            {
                case SyntaxKind.RightParenthesisToken:
                case SyntaxKind.RightBracketToken:
                    depth++;
                    break;

                case SyntaxKind.LeftBracketToken:
                    depth--;
                    break;

                case SyntaxKind.CommaToken when depth == 0:
                    commas++;
                    break;

                case SyntaxKind.LeftCurlyToken:
                case SyntaxKind.RightCurlyToken:
                    // A call's arguments never cross a block boundary.
                    return null;

                case SyntaxKind.LeftParenthesisToken:
                    if (depth > 0)
                    {
                        depth--;
                        break;
                    }

                    if (i == 0 || tokens[i - 1].Kind != SyntaxKind.IdentifierToken)
                    {
                        return null;
                    }

                    var name = tokens[i - 1].Text;
                    var named = NamedArgumentIndex(analysis, name, tokens, i, offset);

                    return (name, named ?? commas);
            }
        }

        return null;
    }

    /// <summary>The parameter a <c>name:</c> in the current argument refers to.</summary>
    private static int? NamedArgumentIndex(Analysis analysis, string functionName,
        IReadOnlyList<SyntaxToken> tokens, int openParenIndex, int offset)
    {
        // Only the argument the cursor is in matters, so scan forward from the last comma at this
        // level rather than over the whole list.
        var depth = 0;
        var start = openParenIndex + 1;

        for (var i = openParenIndex + 1; i < tokens.Count; i++)
        {
            var kind = tokens[i].Kind;

            if (kind is SyntaxKind.LeftParenthesisToken or SyntaxKind.LeftBracketToken)
                depth++;
            else if (kind is SyntaxKind.RightParenthesisToken or SyntaxKind.RightBracketToken)
                depth--;
            else if (kind == SyntaxKind.CommaToken && depth == 0)
                start = i + 1;
        }

        if (start + 1 >= tokens.Count
            || tokens[start].Kind != SyntaxKind.IdentifierToken
            || tokens[start + 1].Kind != SyntaxKind.ColonToken)
        {
            return null;
        }

        var function = analysis.Compilation?.GetSymbols()
            .OfType<FunctionSymbol>()
            .FirstOrDefault(f => f.Name == functionName);

        var parameter = function?.Parameters.FirstOrDefault(p => p.Name == tokens[start].Text);

        return parameter?.Ordinal;
    }
}
