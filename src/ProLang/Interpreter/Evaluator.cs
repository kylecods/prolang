using System.Collections.Immutable;
using ProLang.Intermediate;
using ProLang.Interop;
using ProLang.Symbols;

namespace ProLang.Interpreter;

internal sealed class Evaluator
{
    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, object> _globals;
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = new();
    private readonly Stack<Dictionary<VariableSymbol, object>> _locals = new();

    private object? _lastValue;

    private Random? _random;

    public Evaluator(BoundProgram program, Dictionary<VariableSymbol, object> variables)
    {
        _program = program;
        _globals = variables;
        _locals.Push(new Dictionary<VariableSymbol, object>());

        var current = program;

        while (current != null) 
        {
            foreach (var kv in current.Functions)
            {
                var function = kv.Key;
                var body = kv.Value;

                _functions.Add(function, body);
            }

            current = current.Previous;
        }
    }

    public object? Evaluate()
    {
        var function = _program.MainFunction ?? _program.ScriptFunction;

        if (function == null)
        {
            return null;
        }

        var body = _functions[function];

        return EvaluateStatement(body);
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
                // For string concatenation, convert operands to strings if needed
                var leftStr = leftOperand as string ?? leftOperand?.ToString() ?? "";
                var rightStr = rightOperand as string ?? rightOperand?.ToString() ?? "";
                return leftStr + rightStr;
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
            case BoundNodeKind.BoundArrayExpression:
                return EvaluateArrayExpression((BoundArrayExpression)node);
            case BoundNodeKind.BoundMapExpression:
                return EvaluateMapExpression((BoundMapExpression)node);
            case BoundNodeKind.BoundIndexExpression:
                return EvaluateIndexExpression((BoundIndexExpression)node);
            case BoundNodeKind.BoundIndexAssignmentExpression:
                return EvaluateIndexAssignmentExpression((BoundIndexAssignmentExpression)node);
            case BoundNodeKind.BoundStructCreationExpression:
                return EvaluateStructCreationExpression((BoundStructCreationExpression)node);
            case BoundNodeKind.BoundFieldAccessExpression:
                return EvaluateFieldAccessExpression((BoundFieldAccessExpression)node);
            case BoundNodeKind.BoundFieldAssignmentExpression:
                return EvaluateFieldAssignmentExpression((BoundFieldAssignmentExpression)node);
            default:
                throw new Exception($"Unexpected node {node.Kind}");
        }
    }

    private object EvaluateArrayExpression(BoundArrayExpression node)
    {
        var elements = new List<object>();
        foreach (var element in node.Elements)
        {
            elements.Add(EvaluateExpression(element));
        }
        return elements;
    }

    private object EvaluateMapExpression(BoundMapExpression node)
    {
        var entries = new Dictionary<object, object>();
        foreach (var entry in node.Entries)
        {
            var key = EvaluateExpression(entry.Key);
            var value = EvaluateExpression(entry.Value);
            entries.Add(key, value);
        }
        return entries;
    }

    private object EvaluateIndexExpression(BoundIndexExpression node)
    {
        var expression = EvaluateExpression(node.Expression);
        var index = EvaluateExpression(node.Index);

        if (expression is List<object> array)
        {
            return array[Convert.ToInt32(index)];
        }
        else if (expression is Dictionary<object, object> map)
        {
            return map[index];
        }

        throw new Exception($"Cannot index type {expression?.GetType()}");
    }

    private object EvaluateIndexAssignmentExpression(BoundIndexAssignmentExpression node)
    {
        var lhs = EvaluateExpression(node.LHS);
        var index = EvaluateExpression(node.Index);
        var rhs = EvaluateExpression(node.RHS);

        if (lhs is List<object> array)
        {
            array[Convert.ToInt32(index)] = rhs;
        }
        else if (lhs is Dictionary<object, object> map)
        {
            map[index] = rhs;
        }
        else
        {
            throw new Exception($"Cannot assign to index of type {lhs?.GetType()}");
        }

        return rhs;
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

        if (node.Function == BuiltInFunctions.Push)
        {
            var arr = (List<object>)EvaluateExpression(node.Arguments[0]);
            var value = EvaluateExpression(node.Arguments[1]);
            arr.Add(value);
            return null;
        }

        if (node.Function == BuiltInFunctions.Pop)
        {
            var arr = (List<object>)EvaluateExpression(node.Arguments[0]);
            if (arr.Count == 0)
                throw new Exception("Cannot pop from an empty array.");
            var value = arr[arr.Count - 1];
            arr.RemoveAt(arr.Count - 1);
            return value;
        }

        if (node.Function == BuiltInFunctions.GetAt)
        {
            var arr = (List<object>)EvaluateExpression(node.Arguments[0]);
            var index = (int)EvaluateExpression(node.Arguments[1]);
            if (index < 0 || index >= arr.Count)
                throw new Exception($"Index {index} is out of bounds for array of length {arr.Count}.");
            return arr[index];
        }

        if (node.Function == BuiltInFunctions.Length)
        {
            var arr = (List<object>)EvaluateExpression(node.Arguments[0]);
            return arr.Count;
        }

        // String methods
        if (node.Function == BuiltInFunctions.StringLength)
        {
            var str = (string)EvaluateExpression(node.Arguments[0]);
            return str.Length;
        }

        if (node.Function == BuiltInFunctions.StringCharAt)
        {
            var str = (string)EvaluateExpression(node.Arguments[0]);
            var index = (int)EvaluateExpression(node.Arguments[1]);
            if (index >= 0 && index < str.Length)
                return str[index].ToString();
            return "";
        }

        if (node.Function == BuiltInFunctions.StringSubstring)
        {
            var str = (string)EvaluateExpression(node.Arguments[0]);
            var start = (int)EvaluateExpression(node.Arguments[1]);
            var end = (int)EvaluateExpression(node.Arguments[2]);
            if (start >= 0 && end <= str.Length && start <= end)
                return str.Substring(start, end - start);
            return "";
        }

        if (node.Function == BuiltInFunctions.StringIndexOf)
        {
            var str = (string)EvaluateExpression(node.Arguments[0]);
            var needle = (string)EvaluateExpression(node.Arguments[1]);
            var idx = str.IndexOf(needle);
            return idx >= 0 ? idx : -1;
        }

        // Handle .NET function calls
        if (node.Function is DotNetFunctionSymbol dotNetFunc)
        {
            return EvaluateDotNetCall(dotNetFunc, node.Arguments);
        }

        var locals = new Dictionary<VariableSymbol, object>();

        for (int i = 0; i < node.Arguments.Length; i++)
        {
            var parameter = node.Function.Parameters[i];

            var value = EvaluateExpression(node.Arguments[i]);
            
            locals.Add(parameter,value);
        }
        
        _locals.Push(locals);

        var statement = _functions[node.Function];

        var result = EvaluateStatement(statement);

        _locals.Pop();

        return result;

    }

    /// <summary>
    /// Evaluates a .NET method/constructor call.
    /// </summary>
    private object? EvaluateDotNetCall(DotNetFunctionSymbol dotNetFunc, ImmutableArray<BoundExpression> arguments)
    {
        // Evaluate all arguments
        var args = new object?[arguments.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            args[i] = EvaluateExpression(arguments[i]);
        }

        // Handle constructor calls
        if (dotNetFunc.ConstructorInfo != null)
        {
            var preparedArgs = DotNetTypeMapper.PrepareArguments(args, dotNetFunc.ConstructorInfo.GetParameters());
            var result = dotNetFunc.ConstructorInfo.Invoke(preparedArgs);
            return DotNetTypeMapper.ConvertFromDotNet(result);
        }

        // Handle static method calls
        if (dotNetFunc.MethodInfo != null && dotNetFunc.IsStatic)
        {
            var preparedArgs = DotNetTypeMapper.PrepareArguments(args, dotNetFunc.MethodInfo.GetParameters());
            var result = dotNetFunc.MethodInfo.Invoke(null, preparedArgs);
            return DotNetTypeMapper.ConvertFromDotNet(result);
        }

        // Handle instance method calls
        if (dotNetFunc.MethodInfo != null && !dotNetFunc.IsStatic)
        {
            if (args.Length == 0)
                throw new InvalidOperationException($"Instance method '{dotNetFunc.Name}' requires a receiver");

            var instance = args[0];
            var methodArgs = new object?[args.Length - 1];
            Array.Copy(args, 1, methodArgs, 0, methodArgs.Length);

            var preparedArgs = DotNetTypeMapper.PrepareArguments(methodArgs, dotNetFunc.MethodInfo.GetParameters());
            var result = dotNetFunc.MethodInfo.Invoke(instance, preparedArgs);
            return DotNetTypeMapper.ConvertFromDotNet(result);
        }

        // Handle static field/property access
        if (dotNetFunc.IsStatic && dotNetFunc.MethodInfo == null && dotNetFunc.ConstructorInfo == null)
        {
            var field = dotNetFunc.DeclaringType.GetField(dotNetFunc.Name);
            if (field != null)
            {
                return DotNetTypeMapper.ConvertFromDotNet(field.GetValue(null));
            }

            var property = dotNetFunc.DeclaringType.GetProperty(dotNetFunc.Name);
            if (property != null)
            {
                return DotNetTypeMapper.ConvertFromDotNet(property.GetValue(null));
            }
        }

        throw new InvalidOperationException($"Cannot invoke .NET member '{dotNetFunc.Name}'");
    }
    
    private object EvaluateConversionExpression(BoundConversionExpression node)
    {
        var value = EvaluateExpression(node.Expression);

        if(node.Type == TypeSymbol.Any)
        {
            return value;
        }

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

        if (node.Type == TypeSymbol.Array || node.Type == TypeSymbol.Map)
        {
            return value;
        }

        throw new Exception($"Unexpected type {node.Type}");
    }

    private object EvaluateStructCreationExpression(BoundStructCreationExpression node)
    {
        var fieldValues = new Dictionary<string, object>();

        for (int i = 0; i < node.StructType.Fields.Length; i++)
        {
            var field = node.StructType.Fields[i];
            var value = EvaluateExpression(node.FieldValues[i]);
            fieldValues[field.Name] = value;
        }

        return fieldValues;
    }

    private object EvaluateFieldAccessExpression(BoundFieldAccessExpression node)
    {
        var structInstance = EvaluateExpression(node.Expression);

        if (structInstance is Dictionary<string, object> fields)
        {
            return fields[node.FieldName]!;
        }

        throw new Exception($"Cannot access field on non-struct type {structInstance?.GetType()}");
    }

    private object EvaluateFieldAssignmentExpression(BoundFieldAssignmentExpression node)
    {
        var structInstance = EvaluateExpression(node.Expression);
        var value = EvaluateExpression(node.Value);

        if (structInstance is Dictionary<string, object> fields)
        {
            fields[node.FieldName] = value;
            return value;
        }

        throw new Exception($"Cannot assign to field on non-struct type {structInstance?.GetType()}");
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