using System.Text.RegularExpressions;

namespace ProLang.Syntax;

internal static partial class SyntaxFacts
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^-?\d+$", RegexOptions.Compiled)]
    private static partial Regex NumberPattern();

    private static readonly Dictionary<string, SyntaxKind> KeywordLookup = new(StringComparer.Ordinal)
    {
        { "let", SyntaxKind.LetKeyword },
        { "true", SyntaxKind.TrueKeyword },
        { "false", SyntaxKind.FalseKeyword },
        { "null", SyntaxKind.NullKeyword },
        { "while", SyntaxKind.WhileKeyword },
        { "for", SyntaxKind.ForKeyword },
        { "script", SyntaxKind.ScriptKeyword },
        { "if", SyntaxKind.IfKeyword },
        { "elif", SyntaxKind.ElIfKeyword },
        { "else", SyntaxKind.ElseKeyword },
        { "to", SyntaxKind.ToKeyword },
        { "func", SyntaxKind.FunctionKeyword },
        { "break", SyntaxKind.BreakKeyword },
        { "continue", SyntaxKind.ContinueKeyword },
        { "return", SyntaxKind.ReturnKeyword },
        { "import", SyntaxKind.ImportKeyword },
        { "struct", SyntaxKind.StructKeyword },
        { "as", SyntaxKind.AsKeyword },
    };

    private static readonly Dictionary<SyntaxKind, int> UnaryOperatorPrecedenceMap = new()
    {
        { SyntaxKind.PlusToken, 6 },
        { SyntaxKind.MinusToken, 6 },
        { SyntaxKind.BangToken, 6 },
    };

    private static readonly Dictionary<SyntaxKind, int> BinaryOperatorPrecedenceMap = new()
    {
        { SyntaxKind.StarToken, 5 },
        { SyntaxKind.SlashToken, 5 },
        { SyntaxKind.PercentageToken, 5 },
        { SyntaxKind.PlusToken, 4 },
        { SyntaxKind.MinusToken, 4 },
        { SyntaxKind.LessThanToken, 3 },
        { SyntaxKind.LessThanEqualToken, 3 },
        { SyntaxKind.GreaterThanToken, 3 },
        { SyntaxKind.GreaterThanEqualToken, 3 },
        { SyntaxKind.BangEqualsToken, 3 },
        { SyntaxKind.EqualsEqualsToken, 3 },
        { SyntaxKind.AmpersandAmpersandToken, 2 },
        { SyntaxKind.PipePipeToken, 1 },
    };

    public static int GetUnaryOperatorPrecedence(this SyntaxKind kind)
    {
        return UnaryOperatorPrecedenceMap.TryGetValue(kind, out var precedence) ? precedence : 0;
    }

    public static int GetBinaryOperatorPrecedence(this SyntaxKind kind)
    {
        return BinaryOperatorPrecedenceMap.TryGetValue(kind, out var precedence) ? precedence : 0;
    }
    
     public static string? GetText(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.PlusToken:
                return "+";
             case SyntaxKind.PlusEqualsToken:
                return "+=";
            case SyntaxKind.MinusToken:
                return "-";
            case SyntaxKind.MinusEqualsToken:
                return "-=";
            case SyntaxKind.StarToken:
                return "*";
            case SyntaxKind.StarEqualsToken:
                return "*=";
            case SyntaxKind.SlashToken:
                return "/";
            case SyntaxKind.SlashEqualsToken:
                return "/=";
            case SyntaxKind.BangToken:
                return "!";
            case SyntaxKind.EqualsToken:
                return "=";
            case SyntaxKind.AmpersandAmpersandToken:
                return "&&";
            case SyntaxKind.PipePipeToken:
                return "||";
            case SyntaxKind.HatToken:
                return "^";
            case SyntaxKind.HatEqualsToken:
                return "^=";
            case SyntaxKind.EqualsEqualsToken:
                return "==";
            case SyntaxKind.BangEqualsToken:
                return "!=";
            case SyntaxKind.LeftParenthesisToken:
                return "(";
            case SyntaxKind.RightParenthesisToken:
                return ")";
            case SyntaxKind.FalseKeyword:
                return "false";
            case SyntaxKind.LetKeyword:
                return "let";
            case SyntaxKind.TrueKeyword:
                return "true";
            case SyntaxKind.NullKeyword:
                return "null";
            case SyntaxKind.LessThanToken:
                return "<";
            case SyntaxKind.GreaterThanToken:
                return ">";
            case SyntaxKind.OpenAngleForwardSlashToken:
                return "</";
            case SyntaxKind.ForwardSlashCloseAngleToken:
                return "/>";
            case SyntaxKind.GreaterThanEqualToken:
                return ">=";
            case SyntaxKind.LessThanEqualToken:
                return "<=";
            case SyntaxKind.DollarToken:
                return "$";
            case SyntaxKind.DollarCurlyToken:
                return "${";
            case SyntaxKind.LeftCurlyToken:
                return "{";
            case SyntaxKind.RightCurlyToken:
                return "}";
            case SyntaxKind.LeftBracketToken:
                return "[";
            case SyntaxKind.RightBracketToken:
                return "]";
            case SyntaxKind.ColonToken:
                return ":";
            case SyntaxKind.CommaToken:
                return ",";
            case SyntaxKind.DotToken:
                return ".";
            case SyntaxKind.IfKeyword:
                return "if";
            case SyntaxKind.ElIfKeyword:
                return "elif";
            case SyntaxKind.WhileKeyword:
                return "while";
            case SyntaxKind.ElseKeyword:
                return "else";
            case SyntaxKind.ForKeyword:
                return "for";
            case SyntaxKind.ToKeyword:
                return "to";
            case SyntaxKind.FunctionKeyword:
                return "func";
            case SyntaxKind.BreakKeyword:
                return "break";
            case SyntaxKind.ContinueKeyword:
                return "continue";
            case SyntaxKind.ReturnKeyword:
                return "return";
            case SyntaxKind.ImportKeyword:
                return "import";
            case SyntaxKind.StructKeyword:
                return "struct";
            case SyntaxKind.AsKeyword:
                return "as";
            default:
                return null;
        }
    }

    public static SyntaxKind GetKeywordKind(string text)
    {
        return KeywordLookup.TryGetValue(text, out var kind) ? kind : SyntaxKind.IdentifierToken;
    }
}