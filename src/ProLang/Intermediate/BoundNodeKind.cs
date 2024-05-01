namespace ProLang.Intermediate;

internal enum  BoundNodeKind
{
    //statements
    VariableDeclaration,
    BlockStatement,
    ExpressionStatement,
    IfStatement,
    ElIfStatement,
    WhileStatement,
    ForStatement,
    LabelStatement,
    GotoStatement,
    ConditionalGotoStatement,
    ReturnStatement,

    
    //expressions
    BoundLiteralExpression,
    BoundUnaryExpression,
    BoundBinaryExpression,
    BoundVariableExpression,
    BoundAssignmentExpression,
    BoundErrorExpression,
    BoundCallExpression,
    BoundConversionExpression
}