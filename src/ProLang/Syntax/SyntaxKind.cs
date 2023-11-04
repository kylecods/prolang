namespace ProLang.Syntax;

public enum SyntaxKind
{
    BadToken,
    
    //tokens
    EofToken,
    WhitespaceToken,
    NumberToken,
    StringToken,
    PlusToken,
    MinusToken,
    StarToken,
    SlashToken,
    EqualsToken,
    SemiColonToken,
    
    //keywords
    LetKeyword,
    
    //expressions
    BinaryExpression,
    NumberExpression,
    
}