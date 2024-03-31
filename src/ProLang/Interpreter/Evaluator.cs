using ProLang.Intermediate;
using ProLang.Symbols;

namespace ProLang.Interpreter;

internal sealed class Evaluator
{
    private readonly BoundBlockStatement _root;

    private readonly Dictionary<VariableSymbol, object> _variables;

    private object _lastValue;

    private Random? _random;

    public Evaluator(BoundBlockStatement root, Dictionary<VariableSymbol, object> variables)
    {
        _root = root;
        _variables = variables;
    }

    public object Evaluate()
    {
        var labelToIndex = new Dictionary<BoundLabel, int>();
        for (int i = 0; i < _root.Statements.Length; i++)
        {
            if (_root.Statements[i] is BoundLabelStatement l)
            {
                labelToIndex.Add(l.BoundLabel, i + 1);
            }
        }

        var index = 0;
        while (index < _root.Statements.Length)
        {
            var s = _root.Statements[index];

            switch (s.Kind)
            {
                case BoundNodeKind.VariableDeclaration:
                    EvaluateVariableDeclaration((BoundVariableDeclaration)s);
                    index++;
                    break;
                case BoundNodeKind.ExpressionStatement:
                    EvaluateExpressionStatement((BoundExpressionStatement)s);
                    index++;
                    break;
                case BoundNodeKind.GotoStatement:
                    var gs = (BoundGotoStatement)s;
                    index = labelToIndex[gs.BoundLabel];
                    break;
                case BoundNodeKind.ConditionalGotoStatement:
                    var cgs = (BoundConditionalGotoStatement)s;
                    var condition = (bool)EvaluateExpression(cgs.Condition);
                    if (condition == cgs.JumpIfTrue)
                        index = labelToIndex[cgs.BoundLabel];
                    else
                        index++;
                    break;
                case BoundNodeKind.LabelStatement:
                    index++;
                    break;
                default:
                    throw new Exception($"Unexpected node {s.Kind}");
            }
        }
        

        return _lastValue;
    }
    private void EvaluateVariableDeclaration(BoundVariableDeclaration node)
    {
        var value = EvaluateExpression(node.Initializer);
        _variables[node.Variable] = value;

        _lastValue = value;
    }

    private void EvaluateExpressionStatement(BoundExpressionStatement node)
    {
        _lastValue = EvaluateExpression(node.Expression);
    }

    private object EvaluateLiteralExpression(BoundLiteralExpression literal)
    {
        return literal.Value;
    }

    private object EvaluateVariableExpression(BoundVariableExpression variableExpression)
    {
        return _variables[variableExpression.Variable];
    }
    private object EvaluateAssignmentExpression(BoundAssignmentExpression assignmentExpression)
    {
        var value = EvaluateExpression(assignmentExpression.Expression);
        
        _variables[assignmentExpression.Variable] = value;

        return value;
    }

    private object EvaluateUnaryExpression(BoundUnaryExpression unaryExpression)
    {
        var operand = EvaluateExpression(unaryExpression.Operand);

        switch (unaryExpression.Op.Kind)
        {
            case BoundUnaryOperatorKind.Identity:
                return (int)operand;
            case BoundUnaryOperatorKind.Negation:
                return -(int)operand;
            case BoundUnaryOperatorKind.LogicalNegation:
                return !(bool)operand;
            default:
                throw new Exception($"Unexpected unary operator {unaryExpression.Op}");
        }
    }

    private object EvaluateBinaryExpression(BoundBinaryExpression binaryExpression)
    {
        var leftOperand = EvaluateExpression(binaryExpression.Left);
        var rightOperand = EvaluateExpression(binaryExpression.Right);

        switch (binaryExpression.Op.Kind)
        {
            case BoundBinaryOperatorKind.Addition:
                if (binaryExpression.Type == TypeSymbol.Int)
                {
                    return (int)leftOperand + (int)rightOperand;
                }
                return (string)leftOperand + (string)rightOperand;
            case BoundBinaryOperatorKind.Subtraction:
                return (int)leftOperand - (int)rightOperand;
            case BoundBinaryOperatorKind.Multiplication:
                return (int)leftOperand * (int)rightOperand;
            case BoundBinaryOperatorKind.Division:
                return (int)leftOperand / (int)rightOperand;
            case BoundBinaryOperatorKind.LogicalAnd:
                return (bool)leftOperand && (bool)rightOperand;
            case BoundBinaryOperatorKind.LogicalOr:
                return (bool)leftOperand || (bool)rightOperand;
            case BoundBinaryOperatorKind.Equals:
                return Equals(leftOperand, rightOperand);
            case BoundBinaryOperatorKind.NotEquals:
                return !Equals(leftOperand, rightOperand);
            case BoundBinaryOperatorKind.Modulo:
                return (int)leftOperand % (int)rightOperand;
            case BoundBinaryOperatorKind.GreaterThan:
                return (int)leftOperand > (int)rightOperand;
            case BoundBinaryOperatorKind.GreaterEqual:
                return (int)leftOperand >= (int)rightOperand;
            case BoundBinaryOperatorKind.LessThan:
                return (int)leftOperand < (int)rightOperand;
            case BoundBinaryOperatorKind.LessEqual:
                return (int)leftOperand <= (int)rightOperand;
            case BoundBinaryOperatorKind.BitwiseAnd:
                if (binaryExpression.Type == TypeSymbol.Int)
                {
                    return (int)leftOperand & (int)rightOperand;
                }
                
                return (bool)leftOperand & (bool)rightOperand;
                
            case BoundBinaryOperatorKind.BitwiseOr:
                if (binaryExpression.Type == TypeSymbol.Int)
                {
                    return (int)leftOperand | (int)rightOperand;
                }

                return (bool)leftOperand | (bool)rightOperand;
            case BoundBinaryOperatorKind.BitwiseXor:
                if (binaryExpression.Type == TypeSymbol.Int)
                {
                    return (int)leftOperand ^ (int)rightOperand;
                }

                return (bool)leftOperand ^ (bool)rightOperand;
            default:
                throw new Exception($"Unexpected binary operator {binaryExpression.Op}");
        }
    }

    private object EvaluateExpression(BoundExpression node)
    {
        switch (node.Kind)
        {
            case BoundNodeKind.BoundLiteralExpression:
                return EvaluateLiteralExpression((BoundLiteralExpression)node);
            case BoundNodeKind.BoundVariableExpression:
                return EvaluateVariableExpression((BoundVariableExpression)node);
            case BoundNodeKind.BoundAssignmentExpression:
                return EvaluateAssignmentExpression((BoundAssignmentExpression)node);
            case BoundNodeKind.BoundUnaryExpression:
                return EvaluateUnaryExpression((BoundUnaryExpression)node);
            case BoundNodeKind.BoundBinaryExpression:
                return EvaluateBinaryExpression((BoundBinaryExpression)node);
            case BoundNodeKind.BoundCallExpression:
                return EvaluateCallExpression((BoundCallExpression)node);
            case BoundNodeKind.BoundConversionExpression:
                return EvaluateConversionExpression((BoundConversionExpression)node);
            default:
                throw new Exception($"Unexpected node {node.Kind}");
        }
    }
    
    private object EvaluateCallExpression(BoundCallExpression node)
    {
        if (node.Function == BuiltInFunctions.ReadInput)
        {
            return Console.ReadLine()!;
        }

        if(node.Function == BuiltInFunctions.Print)
        {
            var message = (string)EvaluateExpression(node.Arguments[0]);
            Console.WriteLine(message);
            return null!;
        }
        if (node.Function == BuiltInFunctions.Random)
        {
            var max = (int)EvaluateExpression(node.Arguments[0]);
            
            if (_random == null)
            {
                _random = new Random();
            }

            return _random.Next(max);
        }
        
        throw new Exception($"Unexpected function {node.Function}");
        
    }
    
    private object EvaluateConversionExpression(BoundConversionExpression node)
    {
        var value = EvaluateExpression(node.Expression);

        if (node.Type == TypeSymbol.Bool)
        {
            return Convert.ToBoolean(value);
        }

        if (node.Type == TypeSymbol.Int)
        {
            return Convert.ToInt32(value);
        }

        if (node.Type == TypeSymbol.String)
        {
            return Convert.ToString(value)!;
        }

        throw new Exception($"Unexpected type {node.Type}");
    }
}