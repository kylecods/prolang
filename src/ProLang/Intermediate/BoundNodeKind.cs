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
    
    //expressions
    BoundLiteralExpression,
    BoundUnaryExpression,
    BoundBinaryExpression,
    BoundVariableExpression,
    BoundAssignmentExpression,
}