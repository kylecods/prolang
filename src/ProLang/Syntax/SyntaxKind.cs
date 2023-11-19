namespace ProLang.Syntax;

public enum SyntaxKind
{
    BadToken,
    
    //tokens
    EofToken,
    WhitespaceToken,
    NumberToken,
    IdentifierToken,
    PlusToken,
    MinusToken,
    StarToken,
    SlashToken,
    EqualsToken,
    SemiColonToken,
    LeftParenthesisToken,
    RightParenthesisToken,
    BangToken,
    AmpersandAmpersandToken,
    EqualsEqualsToken,
    PipePipeToken,
    BangEqualsToken,
    
    //keywords
    LetKeyword,
    FalseKeyword,
    TrueKeyword,
    
    //expressions
    BinaryExpression,
    NumberExpression,
    ParethensisExpression,
    LiteralExpression,
    UnaryExpression,
    
    //statements
    VariableDeclaration,
    
}