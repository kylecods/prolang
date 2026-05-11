using System.Collections.Immutable;

namespace ProLang.Intermediate;

internal abstract class BoundTreeRewriter
{
    public virtual BoundStatement RewriteStatement(BoundStatement node)
    {
        switch (node.Kind)
        {
            case BoundNodeKind.BlockStatement:
                return RewriteBlockStatement((BoundBlockStatement)node);
            case BoundNodeKind.VariableDeclaration:
                return RewriteVariableDeclaration((BoundVariableDeclaration)node);
            case BoundNodeKind.IfStatement:
                return RewriteIfStatement((BoundIfStatement)node);
            case BoundNodeKind.WhileStatement:
                return RewriteWhileStatement((BoundWhileStatement)node);
            case BoundNodeKind.ForStatement:
                return RewriteForStatement((BoundForStatement)node);
            case BoundNodeKind.LabelStatement:
                return RewriteLabelStatement((BoundLabelStatement)node);
            case BoundNodeKind.GotoStatement:
                return RewriteGotoStatement((BoundGotoStatement)node);
            case BoundNodeKind.ReturnStatement:
                return RewriteReturnStatement((BoundReturnStatement)node);
            case BoundNodeKind.ConditionalGotoStatement:
                return RewriteConditionalGotoStatement((BoundConditionalGotoStatement)node);
            case BoundNodeKind.ExpressionStatement:
                return RewriteExpressionStatement((BoundExpressionStatement)node);
            default:
                throw new Exception($"Unexpected node: {node.Kind}");
        }
    }

    private BoundStatement RewriteReturnStatement(BoundReturnStatement node)
    {
        var expression = node.Expression == null ? null : RewriteExpression(node.Expression);

        if (expression == node.Expression)
        {
            return node;
        }

        return new BoundReturnStatement(expression);
    }

    protected virtual BoundStatement RewriteBlockStatement(BoundBlockStatement node)
    {
        ImmutableArray<BoundStatement>.Builder builder = null!;

        for (int i = 0; i < node.Statements.Length; i++)
        {
            var oldStatement = node.Statements[i];

            var newStatement = RewriteStatement(oldStatement);

            if (newStatement != oldStatement)
            {
                if (builder == null)
                {
                    builder = ImmutableArray.CreateBuilder<BoundStatement>(node.Statements.Length);

                    for (int j = 0; j < i; j++)
                    {
                        builder.Add(node.Statements[j]);
                    }
                }
            }

            if (builder != null)
            {
                builder.Add(newStatement);
            }
        }

        if (builder == null)
        {
            return node;
        }

        return new BoundBlockStatement(builder.MoveToImmutable());
    }
    
    protected virtual BoundStatement RewriteVariableDeclaration(BoundVariableDeclaration node)
    {
        var initializer = RewriteExpression(node.Initializer);

        if (initializer == node.Initializer)
        {
            return node;
        }

        return new BoundVariableDeclaration(node.Variable, initializer);
    }
    
    protected virtual BoundStatement RewriteIfStatement(BoundIfStatement node)
    {
        var condition = RewriteExpression(node.Condition);
        var body = RewriteStatement(node.Body);
        var elifStatement = node.ElseIfStatement == null ? null : RewriteStatement(node.ElseIfStatement);
        var elseStatement = node.ElseStatement == null ? null : RewriteStatement(node.ElseStatement);

        if (condition == node.Condition && body == node.Body && elifStatement == node.ElseIfStatement && elseStatement == node.ElseStatement)
        {
            return node;
        }

        return new BoundIfStatement(condition,body,elifStatement,elseStatement);
    }
    protected virtual BoundStatement RewriteForStatement(BoundForStatement node)
    {
        var lowerBound = RewriteExpression(node.LowerBound);
        var upperBound = RewriteExpression(node.UpperBound);

        var body = RewriteStatement(node.Body);

        if (lowerBound == node.LowerBound && upperBound == node.UpperBound && body == node.Body)
        {
            return node;
        }

        return new BoundForStatement(node.Variable, lowerBound, upperBound, body, node.BreakLabel,node.ContinueLabel);
    }

    protected virtual BoundStatement RewriteWhileStatement(BoundWhileStatement node)
    {
        var condition = RewriteExpression(node.Condition);

        var body = RewriteStatement(node.Body);

        if (condition == node.Condition && body == node.Body)
        {
            return node;
        }

        return new BoundWhileStatement(condition, body,node.BreakLabel,node.ContinueLabel);
    }
    
    protected virtual BoundStatement RewriteLabelStatement(BoundLabelStatement node)
    {
        return node;
    }
    
    protected virtual BoundStatement RewriteGotoStatement(BoundGotoStatement node)
    {
        return node;
    }
    
    protected virtual BoundStatement RewriteConditionalGotoStatement(BoundConditionalGotoStatement node)
    {
        var condition = RewriteExpression(node.Condition);

        if (condition == node.Condition)
        {
            return node;
        }

        return new BoundConditionalGotoStatement(node.BoundLabel, condition, node.JumpIfTrue);
    }
    
    protected BoundStatement RewriteExpressionStatement(BoundExpressionStatement node)
    {
        var expression = RewriteExpression(node.Expression);

        if (expression == node.Expression)
        {
            return node;
        }

        return new BoundExpressionStatement(expression);
    }

    
    public BoundExpression RewriteExpression(BoundExpression node)
    {
        switch (node.Kind)
        {
            case BoundNodeKind.BoundErrorExpression:
                return RewriteErrorExpression((BoundErrorExpression)node);
            case BoundNodeKind.BoundLiteralExpression:
                return RewriteLiteralExpression((BoundLiteralExpression)node);
            case BoundNodeKind.BoundVariableExpression:
                return RewriteVariableExpression((BoundVariableExpression)node);
            case BoundNodeKind.BoundAssignmentExpression:
                return RewriteAssignmentExpression((BoundAssignmentExpression)node);
            case BoundNodeKind.BoundUnaryExpression:
                return RewriteUnaryExpression((BoundUnaryExpression)node);
            case BoundNodeKind.BoundBinaryExpression:
                return RewriteBinaryExpression((BoundBinaryExpression)node);
            case BoundNodeKind.BoundCallExpression:
                return RewriteCallExpression((BoundCallExpression)node);
            case BoundNodeKind.BoundConversionExpression:
                return RewriteConversionExpression((BoundConversionExpression)node);
            case BoundNodeKind.BoundArrayExpression:
                return RewriteArrayExpression((BoundArrayExpression)node);
            case BoundNodeKind.BoundMapExpression:
                return RewriteMapExpression((BoundMapExpression)node);
            case BoundNodeKind.BoundIndexExpression:
                return RewriteIndexExpression((BoundIndexExpression)node);
            case BoundNodeKind.BoundIndexAssignmentExpression:
                return RewriteIndexAssignmentExpression((BoundIndexAssignmentExpression)node);
            case BoundNodeKind.BoundStructCreationExpression:
                return RewriteStructCreationExpression((BoundStructCreationExpression)node);
            case BoundNodeKind.BoundFieldAccessExpression:
                return RewriteFieldAccessExpression((BoundFieldAccessExpression)node);
            case BoundNodeKind.BoundFieldAssignmentExpression:
                return RewriteFieldAssignmentExpression((BoundFieldAssignmentExpression)node);
            case BoundNodeKind.BoundCastExpression:
                return RewriteCastExpression((BoundCastExpression)node);
            case BoundNodeKind.BoundArrayNewExpression:
                return RewriteArrayNewExpression((BoundArrayNewExpression)node);
            default:
                throw new Exception($"Unexpected node: {node.Kind}");
        }
    }

    protected virtual BoundExpression RewriteArrayExpression(BoundArrayExpression node)
    {
        ImmutableArray<BoundExpression>.Builder? builder = null;

        for (int i = 0; i < node.Elements.Length; i++)
        {
            var oldElement = node.Elements[i];
            var newElement = RewriteExpression(oldElement);
            if (newElement != oldElement)
            {
                if (builder == null)
                {
                    builder = ImmutableArray.CreateBuilder<BoundExpression>(node.Elements.Length);

                    for (int j = 0; j < i; j++)
                    {
                        builder.Add(node.Elements[j]);
                    }
                }
            }

            if (builder != null)
            {
                builder.Add(newElement);
            }
        }

        if (builder == null)
        {
            return node;
        }

        return new BoundArrayExpression(builder.MoveToImmutable(), node.Type);
    }

    protected virtual BoundExpression RewriteMapExpression(BoundMapExpression node)
    {
        ImmutableArray<(BoundExpression Key, BoundExpression Value)>.Builder? builder = null;

        for (int i = 0; i < node.Entries.Length; i++)
        {
            var oldEntry = node.Entries[i];
            var newKey = RewriteExpression(oldEntry.Key);
            var newValue = RewriteExpression(oldEntry.Value);
            if (newKey != oldEntry.Key || newValue != oldEntry.Value)
            {
                if (builder == null)
                {
                    builder = ImmutableArray.CreateBuilder<(BoundExpression Key, BoundExpression Value)>(node.Entries.Length);

                    for (int j = 0; j < i; j++)
                    {
                        builder.Add(node.Entries[j]);
                    }
                }
            }

            if (builder != null)
            {
                builder.Add((newKey, newValue));
            }
        }

        if (builder == null)
        {
            return node;
        }

        return new BoundMapExpression(builder.MoveToImmutable(), node.Type);
    }

    protected virtual BoundExpression RewriteIndexExpression(BoundIndexExpression node)
    {
        var expression = RewriteExpression(node.Expression);
        var index = RewriteExpression(node.Index);

        if (expression == node.Expression && index == node.Index)
        {
            return node;
        }

        return new BoundIndexExpression(expression, index, node.Type);
    }

    protected virtual BoundExpression RewriteIndexAssignmentExpression(BoundIndexAssignmentExpression node)
    {
        var lhs = RewriteExpression(node.LHS);
        var index = RewriteExpression(node.Index);
        var rhs = RewriteExpression(node.RHS);

        if (lhs == node.LHS && index == node.Index && rhs == node.RHS)
        {
            return node;
        }

        return new BoundIndexAssignmentExpression(lhs, index, rhs);
    }

    protected virtual BoundExpression RewriteCallExpression(BoundCallExpression node)
    {
        ImmutableArray<BoundExpression>.Builder? builder = null;

        for (int i = 0; i < node.Arguments.Length; i++)
        {
            var oldArgument = node.Arguments[i];
            var newArgument = RewriteExpression(oldArgument);
            if (newArgument != oldArgument)
            {
                if (builder == null)
                {
                    builder = ImmutableArray.CreateBuilder<BoundExpression>(node.Arguments.Length);

                    for (int j = 0; j < i; j++)
                    {
                        builder.Add(node.Arguments[j]);
                    }
                }
            }

            if (builder != null)
            {
                builder.Add(newArgument);
            }
        }

        if (builder == null)
        {
            return node;
        }

        return new BoundCallExpression(node.Function, builder.MoveToImmutable());
    }
    
    protected virtual BoundExpression RewriteConversionExpression(BoundConversionExpression node)
    {
        var expression = RewriteExpression(node.Expression);

        if (expression == node.Expression)
        {
            return node;
        }

        return new BoundConversionExpression(node.Type, expression);
    }

    protected virtual BoundExpression RewriteLiteralExpression(BoundLiteralExpression node)
    {
        return node;
    }
    
    protected virtual BoundExpression RewriteVariableExpression(BoundVariableExpression node)
    {
        return node;
    }
    
    protected virtual BoundExpression RewriteAssignmentExpression(BoundAssignmentExpression node)
    {
        var expression = RewriteExpression(node.Expression);
        if (expression == node.Expression)
        {
            return node;
        }

        return new BoundAssignmentExpression(node.Variable, expression);
    }
    
    protected virtual BoundExpression RewriteUnaryExpression(BoundUnaryExpression node)
    {
        var operand = RewriteExpression(node.Operand);

        if (operand == node.Operand)
        {
            return node;
        }

        return new BoundUnaryExpression(node.Op, operand);
    }
    
    
    protected virtual BoundExpression RewriteBinaryExpression(BoundBinaryExpression node)
    {
        var left = RewriteExpression(node.Left);
        var right = RewriteExpression(node.Right);

        if (left == node.Left && right == node.Right)
        {
            return node;
        }

        return new BoundBinaryExpression(left, node.Op, right);
    }
    
    protected virtual BoundExpression RewriteErrorExpression(BoundErrorExpression node)
    {
        return node;
    }

    protected virtual BoundExpression RewriteStructCreationExpression(BoundStructCreationExpression node)
    {
        ImmutableArray<BoundExpression>.Builder? builder = null;

        for (int i = 0; i < node.FieldValues.Length; i++)
        {
            var oldFieldValue = node.FieldValues[i];
            var newFieldValue = RewriteExpression(oldFieldValue);
            if (newFieldValue != oldFieldValue)
            {
                if (builder == null)
                {
                    builder = ImmutableArray.CreateBuilder<BoundExpression>(node.FieldValues.Length);

                    for (int j = 0; j < i; j++)
                    {
                        builder.Add(node.FieldValues[j]);
                    }
                }
            }

            if (builder != null)
            {
                builder.Add(newFieldValue);
            }
        }

        if (builder == null)
        {
            return node;
        }

        return new BoundStructCreationExpression(node.StructType, builder.MoveToImmutable());
    }

    protected virtual BoundExpression RewriteFieldAccessExpression(BoundFieldAccessExpression node)
    {
        var expression = RewriteExpression(node.Expression);

        if (expression == node.Expression)
        {
            return node;
        }

        return new BoundFieldAccessExpression(expression, node.FieldName, node.Field);
    }

    protected virtual BoundExpression RewriteFieldAssignmentExpression(BoundFieldAssignmentExpression node)
    {
        var expression = RewriteExpression(node.Expression);
        var value = RewriteExpression(node.Value);

        if (expression == node.Expression && value == node.Value)
        {
            return node;
        }

        return new BoundFieldAssignmentExpression(expression, node.FieldName, node.Field, value);
    }

    protected virtual BoundExpression RewriteCastExpression(BoundCastExpression node)
    {
        var expression = RewriteExpression(node.Expression);

        if (expression == node.Expression)
        {
            return node;
        }

        return new BoundCastExpression(expression, node.TargetType);
    }

    protected virtual BoundExpression RewriteArrayNewExpression(BoundArrayNewExpression node)
    {
        var sizeExpression = RewriteExpression(node.SizeExpression);

        if (sizeExpression == node.SizeExpression)
            return node;

        return new BoundArrayNewExpression(node.ElementType, sizeExpression);
    }
}