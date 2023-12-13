namespace ProLang.Intermediate;

internal enum  BoundNodeKind
{
    //staments
    VariableDeclaration,
    BlockStatement,
    ExpressionStatement,
    
    //expressions
    BoundLiteralExpression,
    BoundUnaryExpression,
    BoundBinaryExpression,
    BoundVariableExpression,
    BoundAssignmentExpression,
}