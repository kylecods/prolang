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

        private MethodReference _consoleWriteLineReference;
        private readonly MethodReference _stringConcatReference;
        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();

        private Dictionary<VariableSymbol, VariableDefinition> _locals = new();

        private TypeDefinition _typeDefinition;

        private Emitter(string moduleName, string[] references)
        {

            var assemblies = new List<AssemblyDefinition>();

            var result = new DiagnosticBag();

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

            _stringConcatReference = ResolveMethod("System.String", "Concat", new[] { "System.String", "System.String" });

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

            foreach(var functionWithBody in program.Functions)
            {
                EmitFunctionDeclaration(functionWithBody.Key);
            }

            foreach (var functionWithBody in program.Functions)
            {
                EmitFunctionBody(functionWithBody.Key, functionWithBody.Value);
            }

            if(program.MainFunction != null)
            {
                _assemblyDefinition.EntryPoint = _methods[program.MainFunction];
            }

            _assemblyDefinition.Write(outputPath);

            return _diagnostics.ToImmutableArray();
        }

        private void EmitFunctionDeclaration(FunctionSymbol function)
        {
            var functionType = _knownTypes[function.Type];

            var method = new MethodDefinition(function.Name, MethodAttributes.Static | MethodAttributes.Private,functionType);


            foreach(var parameter in function.Parameters)
            {
                var parameterType = _knownTypes[parameter.Type];

                var parameterAttributes = ParameterAttributes.None;

                var parameterDefinition = new ParameterDefinition(parameter.Name, parameterAttributes,parameterType);

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

            foreach(var statement in body.Statements)
            {
                EmitStatement(ilProcessor, statement);
            }

            if (function.Type == TypeSymbol.Void) 
            {
                ilProcessor.Emit(OpCodes.Ret);
            }

            method.Body.OptimizeMacros();
        }

        private void EmitStatement(ILProcessor ilProcessor, BoundStatement node)
        {
            switch (node.Kind)
            {
                case BoundNodeKind.VariableDeclaration:
                    EmitVariableDeclaration(ilProcessor,(BoundVariableDeclaration)node);
                    break;
                case BoundNodeKind.LabelStatement:
                    EmitLabelStatement(ilProcessor,(BoundLabelStatement)node);
                    break;
                case BoundNodeKind.GotoStatement:
                    EmitGotoStatement(ilProcessor,(BoundGotoStatement)node);
                    break;
                case BoundNodeKind.ConditionalGotoStatement:
                    EmitConditionalGotoStatement(ilProcessor,(BoundConditionalGotoStatement)node);
                    break;
                case BoundNodeKind.ReturnStatement:
                    EmitReturnStatement(ilProcessor,(BoundReturnStatement)node);
                    break;
                case BoundNodeKind.ExpressionStatement:
                    EmitExpressionStatement(ilProcessor,(BoundExpressionStatement)node);
                    break;
                default:
                    throw new Exception($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitExpressionStatement(ILProcessor ilProcessor, BoundExpressionStatement node)
        {
            EmitExpression(ilProcessor, node.Expression);

            if(node.Expression.Type != TypeSymbol.Void)
            {
                ilProcessor.Emit(OpCodes.Pop);
            }
        }

        private void EmitReturnStatement(ILProcessor ilProcessor, BoundReturnStatement node)
        {
            if(node.Expression != null)
            {
                EmitExpression(ilProcessor, node.Expression);
            }

            ilProcessor.Emit(OpCodes.Ret);
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

            EmitExpression(ilProcessor,node.Initializer);

            ilProcessor.Emit(OpCodes.Stloc, variableDefinition);
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
                    EmitUnaryExpression(ilProcessor,(BoundUnaryExpression)node);
                    break;
                case BoundNodeKind.BoundBinaryExpression:
                    EmitBinaryExpression(ilProcessor,(BoundBinaryExpression)node);
                    break;
                case BoundNodeKind.BoundCallExpression:
                    EmitCallExpression(ilProcessor, (BoundCallExpression)node);
                    break;
                case BoundNodeKind.BoundConversionExpression:
                    EmitConversionExpression(ilProcessor,(BoundConversionExpression)node);
                    break;
                default:
                    throw new NotSupportedException($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitConversionExpression(ILProcessor ilProcessor, BoundConversionExpression node)
        {
            throw new NotImplementedException();
        }

        private void EmitCallExpression(ILProcessor ilProcessor, BoundCallExpression node)
        {
            foreach(var argument in node.Arguments)
            {
                EmitExpression(ilProcessor,argument);
            }

            if (node.Function == BuiltInFunctions.ReadInput) 
            {
                ilProcessor.Emit(OpCodes.Call, _consoleReadLineReference);
            }else if(node.Function == BuiltInFunctions.Print)
            {
                ilProcessor.Emit(OpCodes.Call, _consoleWriteLineReference);
            }else
            {
                var methodDefinition = _methods[node.Function];

                ilProcessor.Emit(OpCodes.Call,methodDefinition);
            }
        }

        private void EmitBinaryExpression(ILProcessor ilProcessor, BoundBinaryExpression node)
        {
            if(node.Op.Kind == BoundBinaryOperatorKind.Addition)
            {
                if(node.Left.Type == TypeSymbol.String && node.Right.Type == TypeSymbol.String)
                {
                    EmitExpression(ilProcessor,node.Left);
                    EmitExpression(ilProcessor,node.Right);
                    ilProcessor.Emit(OpCodes.Call, _stringConcatReference);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }else
            {
                throw new NotImplementedException();
            }
        }

        private void EmitUnaryExpression(ILProcessor ilProcessor, BoundUnaryExpression node)
        {
            throw new NotImplementedException();
        }

        private void EmitAssignmentExpression(ILProcessor ilProcessor, BoundAssignmentExpression node)
        {
            var variableDefinition = _locals[node.Variable];

            EmitExpression(ilProcessor, node.Expression);

            ilProcessor.Emit(OpCodes.Dup);
            ilProcessor.Emit(OpCodes.Stloc, variableDefinition);
        }

        private void EmitVariableExpression(ILProcessor ilProcessor, BoundVariableExpression node)
        {
            if(node.Variable is ParameterSymbol parameter)
            {
                ilProcessor.Emit(OpCodes.Ldarg, parameter.Ordinal);
            }
            else
            {
                var variableDefinition = _locals[node.Variable];

                ilProcessor.Emit(OpCodes.Ldloc, variableDefinition);
            }
        }

        private void EmitLiteralExpression(ILProcessor ilProcessor, BoundLiteralExpression node)
        {
            if(node.Type == TypeSymbol.Bool)
            {
                var value = (bool)node.Value;

                var instruction = value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;

                ilProcessor.Emit(instruction);
            }else if(node.Type == TypeSymbol.Int)
            {
                var value = (int)node.Value;

                ilProcessor.Emit(OpCodes.Ldc_I4,value);
            }else if(node.Type == TypeSymbol.String)
            {
                var value = (string)node.Value;

                ilProcessor.Emit(OpCodes.Ldstr,value);
            }else
            {
                throw new NotImplementedException($"Unexpected literal type: {node.Type}");
            }
        }
    }
}
