using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using ProLang.Intermediate;
using ProLang.Parse;
using ProLang.Symbols;
using System.Collections.Immutable;

namespace ProLang.Compiler
{
    internal sealed class Emitter
    {
        private DiagnosticBag _diagnostics = new();

        private readonly Dictionary<TypeSymbol, TypeReference> _knownTypes;
        private readonly MethodReference _consoleReadLineReference;

        private readonly MethodReference _consoleWriteLineReference;
        private readonly MethodReference _stringConcatReference;
        private readonly MethodReference _minReference;
        private readonly MethodReference _maxReference;

        private readonly MethodReference _arrayListConstructor;
        private readonly MethodReference _arrayListAddReference;
        private readonly MethodReference _arrayListGetReference;
        private readonly MethodReference _arrayListSetReference;

        private readonly MethodReference _hashtableConstructor;
        private readonly MethodReference _hashtableAddReference;
        private readonly MethodReference _hashtableGetReference;
        private readonly MethodReference _hashtableSetReference;

        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();

        private Dictionary<VariableSymbol, VariableDefinition> _locals = new();

        private TypeDefinition _typeDefinition;

        private Emitter(string moduleName, string[] references)
        {

            var assemblies = new List<AssemblyDefinition>();

            foreach (var reference in references)
            {
                try
                {
                    var assembly = AssemblyDefinition.ReadAssembly(reference);

                    assemblies.Add(assembly);
                }
                catch (BadImageFormatException)
                {
                    _diagnostics.ReportInvalidReference(reference);
                }
            }

            var builtInTypes = new List<(TypeSymbol type, string MetadataName)>
            {
                (TypeSymbol.Any, "System.Object"),
                (TypeSymbol.Bool, "System.Boolean"),
                (TypeSymbol.Int, "System.Int32"),
                (TypeSymbol.String, "System.String"),
                (TypeSymbol.Void, "System.Void"),
                (TypeSymbol.Array, "System.Collections.ArrayList"),
                (TypeSymbol.Map, "System.Collections.Hashtable"),
            };

            var assemblyName = new AssemblyNameDefinition(moduleName, new Version(1, 0));

            _assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ModuleKind.Console);

            _knownTypes = new Dictionary<TypeSymbol, TypeReference?>();

            foreach (var (type, metaDataName) in builtInTypes)
            {
                var typeReference = ResolveType(type.Name, metaDataName);

                _knownTypes.Add(type, typeReference);
            }

            TypeReference? ResolveType(string proLangName, string metaDataName)
            {
                var foundTypes = assemblies.SelectMany(a => a.Modules)
                                            .SelectMany(a => a.Types)
                                            .Where(t => t.FullName == metaDataName)
                                            .ToArray();

                if (foundTypes.Length == 1)
                {
                    var typeReference = _assemblyDefinition.MainModule.ImportReference(foundTypes[0]);

                    return typeReference;
                }
                else if (foundTypes.Length == 0)
                {
                    _diagnostics.ReportRequiredTypeNotFound(proLangName, metaDataName);
                }
                else
                {
                    _diagnostics.ReportRequiredTypeAmbiguous(proLangName, metaDataName, foundTypes);
                }

                return null;

            }

            MethodReference? ResolveMethod(string typeName, string methodName, string[] parameterTypeNames)
            {
                var foundTypes = assemblies.SelectMany(a => a.Modules)
                            .SelectMany(a => a.Types)
                            .Where(t => t.FullName == typeName)
                            .ToArray();

                if (foundTypes.Length == 1)
                {
                    var foundType = foundTypes[0];
                    var methods = foundType.Methods.Where(m => m.Name == methodName);

                    foreach (var method in methods)
                    {
                        if (method.Parameters.Count != parameterTypeNames.Length)
                        {
                            continue;
                        }

                        var allParametersMatch = true;

                        for (int i = 0; i < parameterTypeNames.Length; i++)
                        {
                            if (method.Parameters[i].ParameterType.FullName != parameterTypeNames[i])
                            {
                                allParametersMatch = false;
                                break;
                            }
                        }

                        if (!allParametersMatch)
                        {
                            continue;
                        }

                        return _assemblyDefinition.MainModule.ImportReference(method);
                    }

                    _diagnostics.ReportRequiredMethodNotFound(typeName, methodName, parameterTypeNames);

                    return null;
                }
                else if (foundTypes.Length == 0)
                {
                    _diagnostics.ReportRequiredTypeNotFound(null, typeName);
                }
                else
                {
                    _diagnostics.ReportRequiredTypeAmbiguous(null, typeName, foundTypes);
                }

                return null;
            }

            _consoleWriteLineReference = ResolveMethod("System.Console", "WriteLine",
                new[] { "System.String" });

            _consoleReadLineReference = ResolveMethod("System.Console", "ReadLine", Array.Empty<string>());

            _stringConcatReference = ResolveMethod("System.String", "Concat", new[] { "System.Object", "System.Object" });

            _minReference = ResolveMethod("System.Math", "Min", new[] { "System.Int32", "System.Int32" });

            _minReference = ResolveMethod("System.Math", "Max", new[] { "System.Int32", "System.Int32" });

            _arrayListConstructor = ResolveMethod("System.Collections.ArrayList", ".ctor", Array.Empty<string>());
            _arrayListAddReference = ResolveMethod("System.Collections.ArrayList", "Add", new[] { "System.Object" });
            _arrayListGetReference = ResolveMethod("System.Collections.ArrayList", "get_Item", new[] { "System.Int32" });
            _arrayListSetReference = ResolveMethod("System.Collections.ArrayList", "set_Item", new[] { "System.Int32", "System.Object" });

            _hashtableConstructor = ResolveMethod("System.Collections.Hashtable", ".ctor", Array.Empty<string>());
            _hashtableAddReference = ResolveMethod("System.Collections.Hashtable", "Add", new[] { "System.Object", "System.Object" });
            _hashtableGetReference = ResolveMethod("System.Collections.Hashtable", "get_Item", new[] { "System.Object" });
            _hashtableSetReference = ResolveMethod("System.Collections.Hashtable", "set_Item", new[] { "System.Object", "System.Object" });

        }

        public static ImmutableArray<Diagnostic> Emit(BoundProgram program, string moduleName, string[] references, string outputPath)
        {
            if (program.Diagnostics.Any())
            {
                return program.Diagnostics;
            }

            var emitter = new Emitter(moduleName, references);

            return emitter.Emit(program, outputPath);
        }

        public ImmutableArray<Diagnostic> Emit(BoundProgram program, string outputPath)
        {
            if (_diagnostics.Any())
            {
                return _diagnostics.ToImmutableArray();
            }

            var objectType = _knownTypes[TypeSymbol.Any];

            //main class or running class
            _typeDefinition = new TypeDefinition("", "Program", TypeAttributes.Abstract | TypeAttributes.Sealed, objectType);

            _assemblyDefinition.MainModule.Types.Add(_typeDefinition);

            foreach (var functionWithBody in program.Functions)
            {
                EmitFunctionDeclaration(functionWithBody.Key);
            }

            foreach (var functionWithBody in program.Functions)
            {
                EmitFunctionBody(functionWithBody.Key, functionWithBody.Value);
            }

            if (program.MainFunction != null)
            {
                _assemblyDefinition.EntryPoint = _methods[program.MainFunction];
            }

            _assemblyDefinition.Write(outputPath);

            return _diagnostics.ToImmutableArray();
        }

        private void EmitFunctionDeclaration(FunctionSymbol function)
        {
            var functionType = _knownTypes[function.Type];

            var method = new MethodDefinition(function.Name, MethodAttributes.Static | MethodAttributes.Public, functionType);


            foreach (var parameter in function.Parameters)
            {
                var parameterType = _knownTypes[parameter.Type];

                var parameterAttributes = ParameterAttributes.None;

                var parameterDefinition = new ParameterDefinition(parameter.Name, parameterAttributes, parameterType);

                method.Parameters.Add(parameterDefinition);
            }

            _typeDefinition.Methods.Add(method);

            _methods.Add(function, method);

        }

        private void EmitFunctionBody(FunctionSymbol function, BoundBlockStatement body)
        {
            var method = _methods[function];

            _locals.Clear();

            var ilProcessor = method.Body.GetILProcessor();
            foreach (var statement in body.Statements)
            {
                EmitStatement(ilProcessor, statement);
            }

            if (method.ReturnType.FullName == "System.Void")
            {
                EmitInstruction(ilProcessor, OpCodes.Ret);
            }

            method.Body.OptimizeMacros();
        }

        private void EmitStatement(ILProcessor ilProcessor, BoundStatement node)
        {
            switch (node.Kind)
            {
                case BoundNodeKind.VariableDeclaration:
                    EmitVariableDeclaration(ilProcessor, (BoundVariableDeclaration)node);
                    break;
                case BoundNodeKind.LabelStatement:
                    EmitLabelStatement(ilProcessor, (BoundLabelStatement)node);
                    break;
                case BoundNodeKind.GotoStatement:
                    EmitGotoStatement(ilProcessor, (BoundGotoStatement)node);
                    break;
                case BoundNodeKind.ConditionalGotoStatement:
                    EmitConditionalGotoStatement(ilProcessor, (BoundConditionalGotoStatement)node);
                    break;
                case BoundNodeKind.ReturnStatement:
                    EmitReturnStatement(ilProcessor, (BoundReturnStatement)node);
                    break;
                case BoundNodeKind.ExpressionStatement:
                    EmitExpressionStatement(ilProcessor, (BoundExpressionStatement)node);
                    break;
                default:
                    throw new Exception($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitExpressionStatement(ILProcessor ilProcessor, BoundExpressionStatement node)
        {
            EmitExpression(ilProcessor, node.Expression);

            if (node.Expression.Type != TypeSymbol.Void)
            {
                EmitInstruction(ilProcessor, OpCodes.Pop);
            }
        }

        private void EmitReturnStatement(ILProcessor ilProcessor, BoundReturnStatement node)
        {
            if (node.Expression != null)
            {
                EmitExpression(ilProcessor, node.Expression);
            }

            EmitInstruction(ilProcessor, OpCodes.Ret);
        }

        private void EmitConditionalGotoStatement(ILProcessor ilProcessor, BoundConditionalGotoStatement node)
        {
            throw new NotImplementedException();
        }

        private void EmitGotoStatement(ILProcessor ilProcessor, BoundGotoStatement node)
        {
            throw new NotImplementedException();
        }

        private void EmitLabelStatement(ILProcessor ilProcessor, BoundLabelStatement node)
        {
            throw new NotImplementedException();
        }

        private void EmitVariableDeclaration(ILProcessor ilProcessor, BoundVariableDeclaration node)
        {
            var typeReference = _knownTypes[node.Variable.Type];

            var variableDefinition = new VariableDefinition(typeReference);

            _locals.Add(node.Variable, variableDefinition);

            ilProcessor.Body.Variables.Add(variableDefinition);

            EmitExpression(ilProcessor, node.Initializer);

            EmitInstruction(ilProcessor, OpCodes.Stloc, variableDefinition);
        }


        private void EmitExpression(ILProcessor ilProcessor, BoundExpression node)
        {
            switch (node.Kind)
            {
                case BoundNodeKind.BoundLiteralExpression:
                    EmitLiteralExpression(ilProcessor, (BoundLiteralExpression)node);
                    break;
                case BoundNodeKind.BoundVariableExpression:
                    EmitVariableExpression(ilProcessor, (BoundVariableExpression)node);
                    break;
                case BoundNodeKind.BoundAssignmentExpression:
                    EmitAssignmentExpression(ilProcessor, (BoundAssignmentExpression)node);
                    break;
                case BoundNodeKind.BoundUnaryExpression:
                    EmitUnaryExpression(ilProcessor, (BoundUnaryExpression)node);
                    break;
                case BoundNodeKind.BoundBinaryExpression:
                    EmitBinaryExpression(ilProcessor, (BoundBinaryExpression)node);
                    break;
                case BoundNodeKind.BoundCallExpression:
                    EmitCallExpression(ilProcessor, (BoundCallExpression)node);
                    break;
                case BoundNodeKind.BoundConversionExpression:
                    EmitConversionExpression(ilProcessor, (BoundConversionExpression)node);
                    break;
                case BoundNodeKind.BoundArrayExpression:
                    EmitArrayExpression(ilProcessor, (BoundArrayExpression)node);
                    break;
                case BoundNodeKind.BoundMapExpression:
                    EmitMapExpression(ilProcessor, (BoundMapExpression)node);
                    break;
                case BoundNodeKind.BoundIndexExpression:
                    EmitIndexExpression(ilProcessor, (BoundIndexExpression)node);
                    break;
                case BoundNodeKind.BoundIndexAssignmentExpression:
                    EmitIndexAssignmentExpression(ilProcessor, (BoundIndexAssignmentExpression)node);
                    break;
                default:
                    throw new NotSupportedException($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitIndexAssignmentExpression(ILProcessor ilProcessor, BoundIndexAssignmentExpression node)
        {
            EmitExpression(ilProcessor, node.LHS);
            EmitExpression(ilProcessor, node.Index);
            EmitExpression(ilProcessor, node.RHS);

            if (node.LHS.Type == TypeSymbol.Array)
            {
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _arrayListSetReference);
            }
            else
            {
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _hashtableSetReference);
            }
            
            // Push a dummy value because the expression is expected to return something
            EmitInstruction(ilProcessor, OpCodes.Ldnull); 
        }

        private void EmitIndexExpression(ILProcessor ilProcessor, BoundIndexExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);
            EmitExpression(ilProcessor, node.Index);
            
            if (node.Expression.Type == TypeSymbol.Array)
            {
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _arrayListGetReference);
            }
            else
            {
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _hashtableGetReference);
            }
        }

        private void EmitMapExpression(ILProcessor ilProcessor, BoundMapExpression node)
        {
            EmitInstruction(ilProcessor, OpCodes.Newobj, _hashtableConstructor);
            foreach (var entry in node.Entries)
            {
                EmitInstruction(ilProcessor, OpCodes.Dup);
                EmitExpression(ilProcessor, entry.Key);
                EmitExpression(ilProcessor, entry.Value);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _hashtableAddReference);
            }
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode)
        {
            ilProcessor.Emit(opCode);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, MethodReference method)
        {
            ilProcessor.Emit(opCode, method);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, TypeReference type)
        {
            ilProcessor.Emit(opCode, type);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, VariableDefinition variable)
        {
            ilProcessor.Emit(opCode, variable);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, int value)
        {
            ilProcessor.Emit(opCode, value);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, string value)
        {
            ilProcessor.Emit(opCode, value);
        }

        private void EmitArrayExpression(ILProcessor ilProcessor, BoundArrayExpression node)
        {
            EmitInstruction(ilProcessor, OpCodes.Newobj, _arrayListConstructor);
            foreach (var element in node.Elements)
            {
                EmitInstruction(ilProcessor, OpCodes.Dup);
                EmitExpression(ilProcessor, element);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _arrayListAddReference);
                EmitInstruction(ilProcessor, OpCodes.Pop); // ArrayList.Add returns the index, we don't need it.
            }
        }

        private void EmitConversionExpression(ILProcessor ilProcessor, BoundConversionExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);

            var fromType = node.Expression.Type;
            var toType = node.Type;

            if (fromType == TypeSymbol.Any || toType == TypeSymbol.Any)
            {
                // handle boxing
                if (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, _knownTypes[fromType]);
                }
                else if (toType == TypeSymbol.Int || toType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Unbox_Any, _knownTypes[toType]);
                }
            }
            else if (toType == TypeSymbol.String && (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool || fromType == TypeSymbol.Any))
            {
                // we should call ToString()
                // For now, let's just use Convert.ToString if we had it, but we can call Object.ToString()
                // Actually, let's keep it simple. If it's a value type, box it first.
                if (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, _knownTypes[fromType]);
                }
                
                var toStringMethod = _knownTypes[TypeSymbol.Any].Resolve().Methods.First(m => m.Name == "ToString" && m.Parameters.Count == 0);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _assemblyDefinition.MainModule.ImportReference(toStringMethod));
            }
        }

        private void EmitCallExpression(ILProcessor ilProcessor, BoundCallExpression node)
        {
            foreach (var argument in node.Arguments)
            {
                EmitExpression(ilProcessor, argument);
            }

            if (node.Function == BuiltInFunctions.ReadInput)
            {
                EmitInstruction(ilProcessor, OpCodes.Call, _consoleReadLineReference);
            }
            else if (node.Function == BuiltInFunctions.Print)
            {
                EmitInstruction(ilProcessor, OpCodes.Call, _consoleWriteLineReference);
            }
            else if (node.Function == BuiltInFunctions.Min)
            {
                EmitInstruction(ilProcessor, OpCodes.Call, _minReference);
            }
            else if (node.Function == BuiltInFunctions.Max)
            {
                EmitInstruction(ilProcessor, OpCodes.Call, _maxReference);
            }
            else
            {
                var methodDefinition = _methods[node.Function];

                EmitInstruction(ilProcessor, OpCodes.Call, methodDefinition);
            }
        }

        private void EmitBinaryExpression(ILProcessor ilProcessor, BoundBinaryExpression node)
        {
            EmitExpression(ilProcessor, node.Left);
            if (node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String)
            {
                if (node.Left.Type != TypeSymbol.String && node.Left.Type != TypeSymbol.Any)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, _knownTypes[node.Left.Type]);
                }
            }

            EmitExpression(ilProcessor, node.Right);
            if (node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String)
            {
                if (node.Right.Type != TypeSymbol.String && node.Right.Type != TypeSymbol.Any)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, _knownTypes[node.Right.Type]);
                }
            }

            if (node.Op.Kind == BoundBinaryOperatorKind.Addition)
            {
                if (node.Op.Type == TypeSymbol.String)
                {
                    EmitInstruction(ilProcessor, OpCodes.Call, _stringConcatReference);
                }
                else
                {
                    EmitInstruction(ilProcessor, OpCodes.Add);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Subtraction)
            {
                EmitInstruction(ilProcessor, OpCodes.Sub);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Multiplication)
            {
                EmitInstruction(ilProcessor, OpCodes.Mul);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Division)
            {
                EmitInstruction(ilProcessor, OpCodes.Div);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LogicalAnd)
            {
                EmitInstruction(ilProcessor, OpCodes.And);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LogicalOr)
            {
                EmitInstruction(ilProcessor, OpCodes.Or);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Equals)
            {
                EmitInstruction(ilProcessor, OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.NotEquals)
            {
                EmitInstruction(ilProcessor, OpCodes.Ceq);
                EmitInstruction(ilProcessor, OpCodes.Ldc_I4_0);
                EmitInstruction(ilProcessor, OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LessThan)
            {
                EmitInstruction(ilProcessor, OpCodes.Clt);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LessEqual)
            {
                EmitInstruction(ilProcessor, OpCodes.Cgt);
                EmitInstruction(ilProcessor, OpCodes.Ldc_I4_0);
                EmitInstruction(ilProcessor, OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.GreaterThan)
            {
                EmitInstruction(ilProcessor, OpCodes.Cgt);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.GreaterEqual)
            {
                EmitInstruction(ilProcessor, OpCodes.Clt);
                EmitInstruction(ilProcessor, OpCodes.Ldc_I4_0);
                EmitInstruction(ilProcessor, OpCodes.Ceq);
            }
            else
            {
                throw new Exception($"Unexpected binary operator {node.Op.Kind}");
            }
        }

        private void EmitUnaryExpression(ILProcessor ilProcessor, BoundUnaryExpression node)
        {
            EmitExpression(ilProcessor, node.Operand);

            if (node.Op.Kind == BoundUnaryOperatorKind.Identity)
            {
                //do nothing
            }
            else if (node.Op.Kind == BoundUnaryOperatorKind.Negation)
            {
                EmitInstruction(ilProcessor, OpCodes.Neg);
            }
            else if (node.Op.Kind == BoundUnaryOperatorKind.LogicalNegation)
            {
                EmitInstruction(ilProcessor, OpCodes.Ldc_I4_0);
                EmitInstruction(ilProcessor, OpCodes.Ceq);
            }
            else
            {
                throw new Exception($"Unexpected unary operator {node.Op.Kind}");
            }
        }

        private void EmitAssignmentExpression(ILProcessor ilProcessor, BoundAssignmentExpression node)
        {
            var variableDefinition = _locals[node.Variable];

            EmitExpression(ilProcessor, node.Expression);

            EmitInstruction(ilProcessor, OpCodes.Dup);
            EmitInstruction(ilProcessor, OpCodes.Stloc, variableDefinition);
        }

        private void EmitVariableExpression(ILProcessor ilProcessor, BoundVariableExpression node)
        {
            if (node.Variable is ParameterSymbol parameter)
            {
                EmitInstruction(ilProcessor, OpCodes.Ldarg, parameter.Ordinal);
            }
            else
            {
                var variableDefinition = _locals[node.Variable];

                EmitInstruction(ilProcessor, OpCodes.Ldloc, variableDefinition);
            }
        }

        private void EmitLiteralExpression(ILProcessor ilProcessor, BoundLiteralExpression node)
        {
            if (node.Type == TypeSymbol.Bool)
            {
                var value = (bool)node.Value;

                var instruction = value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;

                EmitInstruction(ilProcessor, instruction);
            }
            else if (node.Type == TypeSymbol.Int)
            {
                var value = (int)node.Value;

                EmitInstruction(ilProcessor, OpCodes.Ldc_I4, value);
            }
            else if (node.Type == TypeSymbol.String)
            {
                var value = (string)node.Value;

                EmitInstruction(ilProcessor, OpCodes.Ldstr, value);
            }
            else
            {
                throw new NotImplementedException($"Unexpected literal type: {node.Type}");
            }
        }
    }
}
