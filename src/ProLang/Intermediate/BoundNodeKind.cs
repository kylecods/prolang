namespace ProLang.Intermediate;

internal enum  BoundNodeKind
{
    //staments
    VariableDeclaration,
    BlockStatement,
    ExpressionStatement,
    IfStatement,
    ElIfStatement,
    
    //expressions
    BoundLiteralExpression,
    BoundUnaryExpression,
    BoundBinaryExpression,
    BoundVariableExpression,
    BoundAssignmentExpression,
}