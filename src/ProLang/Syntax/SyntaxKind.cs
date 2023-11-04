namespace ProLang.Syntax;

public enum SyntaxKind
{
    BadToken,
    
    //tokens
    EofToken,
    NumberToken,
    StringToken,
    PlusToken,
    MinusToken,
    StarToken,
    SlashToken,
    EqualsToken,
    SemiColonToken,
    
    //keywords
    LetKeyword
    
}