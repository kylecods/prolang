﻿namespace ProLang.Syntax;

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
    
    //html keywords
    ScriptKeyword,
    
    //expressions
    BinaryExpression,
    NumberExpression,
    ParethensisExpression,
    LiteralExpression,
    UnaryExpression,
    AssignmentExpression,
    
    //statements
    VariableDeclaration,
    ExpressionStatement,
    HtmlStatement,
    ProLangBlockStatement,
    
    //nodes
    GlobalDeclaration,
    GlobalStatement,
    HtmlDeclaration,
    
    //html groups
    HtmlStartTag,
    HtmlEndTag,
    
}