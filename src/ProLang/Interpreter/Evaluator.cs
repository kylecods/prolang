using ProLang.Intermediate;
using ProLang.Symbols;

namespace ProLang.Interpreter;

internal sealed class Evaluator
{
    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, object> _globals;
    private readonly Stack<Dictionary<VariableSymbol, object>> _locals = new();

    private object? _lastValue;

    private Random? _random;

    public Evaluator(BoundProgram program, Dictionary<VariableSymbol, object> variables)
    {
        _program = program;
        _globals = variables;
        _locals.Push(new Dictionary<VariableSymbol, object>());
    }

    public object? Evaluate()
    {
        return EvaluateStatement(_program.Statement);
    }

    private object? EvaluateStatement(BoundBlockStatement body)
    {
        var labelToIndex = new Dictionary<BoundLabel, int>();
        for (int i = 0; i < body.Statements.Length; i++)
        {
            if (body.Statements[i] is BoundLabelStatement l)
            {
                labelToIndex.Add(l.BoundLabel, i + 1);
            }
        }

        var index = 0;
        while (index < body.Statements.Length)
        {
            var s = body.Statements[index];

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
                case BoundNodeKind.ReturnStatement:
                    var rs = (BoundReturnStatement)s;
                    _lastValue = rs.Expression == null ? null : EvaluateExpression(rs.Expression);
                    return _lastValue;
                default:
                    throw new Exception($"Unexpected node {s.Kind}");
            }
        }
        

        return _lastValue;
    }

    private void EvaluateVariableDeclaration(BoundVariableDeclaration node)
    {
        var value = EvaluateExpression(node.Initializer);
        
        _lastValue = value;

        Assign(node.Variable, value);
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
        if (variableExpression.Variable.Kind == SymbolKind.GlobalVariable)
        {
            return _globals[variableExpression.Variable];
        }

        var locals = _locals.Peek();

        return locals[variableExpression.Variable];
    }
    private object EvaluateAssignmentExpression(BoundAssignmentExpression assignmentExpression)
    {
        var value = EvaluateExpression(assignmentExpression.Expression);

        Assign(assignmentExpression.Variable, value);

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
                return EvaluateCallExpression((BoundCallExpression)node)!;
            case BoundNodeKind.BoundConversionExpression:
                return EvaluateConversionExpression((BoundConversionExpression)node);
            default:
                throw new Exception($"Unexpected node {node.Kind}");
        }
    }
    
    private object? EvaluateCallExpression(BoundCallExpression node)
    {
        if (node.Function == BuiltInFunctions.ReadInput)
        {
            return Console.ReadLine()!;
        }

        if(node.Function == BuiltInFunctions.Print)
        {
            var message = (string)EvaluateExpression(node.Arguments[0]);
            Console.WriteLine(message);
            return null;
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

        if (node.Function == BuiltInFunctions.Min)
        {
            var arg1 = (int)EvaluateExpression(node.Arguments[0]);

            var arg2 = (int)EvaluateExpression(node.Arguments[1]);

            return Math.Min(arg1, arg2);
        }
        
        if (node.Function == BuiltInFunctions.Max)
        {
            var arg1 = (int)EvaluateExpression(node.Arguments[0]);

            var arg2 = (int)EvaluateExpression(node.Arguments[1]);

            return Math.Max(arg1, arg2);
        }

        if (node.Function == BuiltInFunctions.FileExists)
        {
            var path = (string)EvaluateExpression(node.Arguments[0]);

            return File.Exists(path);
        }

        if (node.Function == BuiltInFunctions.ReadFile)
        {
            var path = (string)EvaluateExpression(node.Arguments[0]);

            return File.ReadAllText(path);
        }
        
        if (node.Function == BuiltInFunctions.WriteFile)
        {
            var path = (string)EvaluateExpression(node.Arguments[0]);
            
            var contents = (string)EvaluateExpression(node.Arguments[1]);

             File.WriteAllText(path,contents);

             return null;
        }


        var locals = new Dictionary<VariableSymbol, object>();

        for (int i = 0; i < node.Arguments.Length; i++)
        {
            var parameter = node.Function.Parameters[i];

            var value = EvaluateExpression(node.Arguments[i]);
            
            locals.Add(parameter,value);
        }
        
        _locals.Push(locals);

        var statement = _program.Functions[node.Function];

        var result = EvaluateStatement(statement);

        _locals.Pop();

        return result;

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

    private void Assign(VariableSymbol variable, object value)
    {
        if (variable.Kind == SymbolKind.GlobalVariable)
        {
            _globals[variable] = value;
        }
        else
        {
            var locals = _locals.Peek();

            locals[variable] = value;
        }
    }
}