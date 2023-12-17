namespace ProLang.Intermediate;

internal enum  BoundNodeKind
{
    //staments
    VariableDeclaration,
    BlockStatement,
    ExpressionStatement,
    IfStatement,
    ElIfStatement,
    WhileStatement,
    ForStatement,
    
    //expressions
    BoundLiteralExpression,
    BoundUnaryExpression,
    BoundBinaryExpression,
    BoundVariableExpression,
    BoundAssignmentExpression,
}