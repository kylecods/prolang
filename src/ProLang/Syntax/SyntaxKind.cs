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
    PlusEqualsToken,
    MinusEqualsToken,
    SlashEqualsToken,
    StarEqualsToken,
    HatToken,
    HatEqualsToken,
    LeftAngleBracketToken,
    RightAngleBracketToken,
    
    //keywords
    LetKeyword,
    FalseKeyword,
    TrueKeyword,
    WhileKeyword,
    ForKeyword,
    
    //expressions
    BinaryExpression,
    NumberExpression,
    ParethensisExpression,
    LiteralExpression,
    UnaryExpression,
    
    //statements
    VariableDeclaration,
    HtmlStatement,
    
}