namespace ProLang.Intermediate;

public enum  BoundNodeKind
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
    BoundNullExpression,
    BoundUnaryExpression,
    BoundBinaryExpression,
    BoundVariableExpression,
    BoundAssignmentExpression,
    BoundErrorExpression,
    BoundCallExpression,
    BoundConversionExpression,
    BoundArrayExpression,
    BoundMapExpression,
    BoundIndexExpression,
    BoundIndexAssignmentExpression,
    BoundStructCreationExpression,
    BoundFieldAccessExpression,
    BoundFieldAssignmentExpression,
    BoundCastExpression,
    BoundArrayNewExpression,
    BoundEnumMemberExpression,
    BoundFunctionReference,
    BoundIndirectCallExpression,
}