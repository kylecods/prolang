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
    LessThanToken,//Can be used as open angle '<'div..
    GreaterThanToken,//Can be used as open angle ..div'>'
    LessThanEqualToken,
    GreaterThanEqualToken,
    ForwardSlashCloseAngleToken, // <input '/>'
    OpenAngleForwardSlashToken, //<div> '</' div>
    
    //keywords
    LetKeyword,
    FalseKeyword,
    TrueKeyword,
    WhileKeyword,
    ForKeyword,
    ScriptKeyword,
    
    //expressions
    BinaryExpression,
    NumberExpression,
    ParethensisExpression,
    LiteralExpression,
    UnaryExpression,
    
    //statements
    VariableDeclaration,
    ExpressionStatement,
    HtmlStatement,
    
    //nodes
    GlobalDeclaration,
    GlobalStatement,
    HtmlDeclaration,
    
    //groups
    HtmlStartTag,
    HtmlEndTag,
    
}