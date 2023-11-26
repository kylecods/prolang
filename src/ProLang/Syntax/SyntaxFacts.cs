namespace ProLang.Syntax;

internal static class SyntaxFacts
{
    public static int GetUnaryOperatorPrecedence(this SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
            case SyntaxKind.BangToken:
                return 6;
            default:
                return 0;
        }
    }
    
    public static int GetBinaryOperatorPrecedence(this SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.StarToken:
            case SyntaxKind.SlashToken:
                return 5;
                
            case SyntaxKind.PlusToken:
            case SyntaxKind.MinusToken:
                return 4;
            case SyntaxKind.BangEqualsToken:
            case SyntaxKind.EqualsEqualsToken:
                return 3;
            
            case SyntaxKind.AmpersandAmpersandToken:
                return 2;
            
            case SyntaxKind.PipePipeToken:
                return 1;
            
            default: return 0;
        }
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
            default:
                return null;
        }
    }

     public static SyntaxKind GetKeywordKind(string text)
     {
         switch (text)
         {
            case "let":
                return SyntaxKind.LetKeyword;
            case "true":
                return SyntaxKind.TrueKeyword;
            case "false":
                return SyntaxKind.FalseKeyword;
            case "while":
                return SyntaxKind.WhileKeyword;
            case "for":
                return SyntaxKind.ForKeyword;
            default:
                return SyntaxKind.IdentifierToken;
         }
     }
}