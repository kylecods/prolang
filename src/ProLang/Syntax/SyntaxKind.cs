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
    LeftBracketToken, // '['
    RightBracketToken, // ']'
    HatToken, // '^'
    LessThanToken,//Can be used as open angle in html '<'div..
    GreaterThanToken,//Can be used as open angle in html ..div'>'
    DollarToken,// '$"
    
    AmpersandAmpersandToken, // '&&'
    PipePipeToken, // '||'
    DotToken, // '.'
    
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
    NullKeyword,
    WhileKeyword,
    ForKeyword,
    IfKeyword,
    ElIfKeyword,
    ElseKeyword,
    ToKeyword,
    FunctionKeyword,
    BreakKeyword,
    ContinueKeyword,
    ReturnKeyword,
    ImportKeyword,
    StructKeyword,
    AsKeyword,

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
    ArrayExpression,
    MapExpression,
    IndexExpression,
    MethodCallExpression,
    MapEntry,
    CastExpression,
    
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
    ReturnStatement,
    
    
    //nodes
    GlobalDeclaration,
    GlobalStatement,
    HtmlDeclaration,
    FunctionDeclaration,
    ImportDeclaration,
    TypeClause,
    NameType,
    GenericType,
    ArrayType,
    Parameter,
    StructDeclaration,
    FieldDeclaration,
    StructCreationExpression,
    FieldInitializer,
    FieldAccessExpression,
    
    //html groups
    HtmlStartTag,
    HtmlEndTag,
}