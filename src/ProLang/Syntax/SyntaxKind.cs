namespace ProLang.Syntax;

public enum SyntaxKind
{
    BadToken,
    
    //tokens
    EofToken, // '\0'
    WhitespaceToken,// '\t', '\r', ' '
    NumberToken,// '1'
    StringToken,//"test"
    IdentifierToken,//foo
    
    PlusToken,// '+'
    MinusToken,// '-'
    StarToken,// '*'
    SlashToken,// '/'
    BangToken,// '!'
    EqualsToken,// '='
    SemiColonToken,// ';'
    PercentageToken,// '%',
    TildeToken,//'~'
    AmpersandToken,//'&'
    PipeToken,//'|'
    CommaToken,//','
    ColonToken,//':'

    
    LeftParenthesisToken,// '('
    RightParenthesisToken,// ')'
    LeftCurlyToken,// '{'
    RightCurlyToken,// '}'
    HatToken, // '^'
    LessThanToken,//Can be used as open angle in html '<'div..
    GreaterThanToken,//Can be used as open angle in html ..div'>'
    DollarToken,// '$"
    
    AmpersandAmpersandToken, // '&&'
    PipePipeToken, // '||'
    
    EqualsEqualsToken,// '=='
    LessThanEqualToken,// '<='
    GreaterThanEqualToken,// '>='
    
    BangEqualsToken, // '!='
    PlusEqualsToken, // '+='
    MinusEqualsToken,// '-='
    SlashEqualsToken,// '/='
    StarEqualsToken,// '*='
    HatEqualsToken,// '^='
    
    //html tokens
    ForwardSlashCloseAngleToken, // <input '/>'
    OpenAngleForwardSlashToken, //<div> '</' div>
    DollarCurlyToken,// '${'
    
    //keywords
    LetKeyword,
    FalseKeyword,
    TrueKeyword,
    WhileKeyword,
    ForKeyword,
    IfKeyword,
    ElIfKeyword,
    ElseKeyword,
    ToKeyword,
    FunctionKeyword,
    BreakKeyword,
    ContinueKeyword,
    
    //html keywords
    ScriptKeyword,
    
    //expressions
    BinaryExpression,
    NumberExpression,
    ParethensisExpression,
    LiteralExpression,
    UnaryExpression,
    AssignmentExpression,
    NameExpression,
    CallExpression,
    
    //statements
    VariableDeclaration,
    ExpressionStatement,
    HtmlStatement,
    ProLangBlockStatement,
    BlockStatement,
    IfStatement,
    WhileStatement,
    ElseClause,
    ElseIfClause,
    ForStatement,
    BreakStatement,
    ContinueStatement,
    
    
    //nodes
    GlobalDeclaration,
    GlobalStatement,
    HtmlDeclaration,
    FunctionDeclaration,
    TypeClause,
    Parameter,
    
    //html groups
    HtmlStartTag,
    HtmlEndTag,
}