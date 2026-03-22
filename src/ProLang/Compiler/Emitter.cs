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

        private readonly Dictionary<TypeSymbol, TypeReference> _knownTypes = new();
        private readonly List<AssemblyDefinition> _assemblies = new();
        
        private readonly MethodReference _consoleReadLineReference;
        private readonly MethodReference _consoleWriteLineReference;
        private readonly MethodReference _stringConcatReference;
        private readonly MethodReference _minReference;
        private readonly MethodReference _maxReference;

        private MethodReference? _listAddMethod;
        private MethodReference? _listRemoveAtMethod;
        private MethodReference? _listGetCountMethod;

        private readonly TypeReference _listType;
        private readonly TypeReference _dictionaryType;

        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();

        private Dictionary<VariableSymbol, VariableDefinition> _locals = new();

        private TypeDefinition _typeDefinition;

        private Dictionary<BoundLabel, Instruction> _labels = new();
        private List<(BoundLabel label, Instruction instruction)> _fixups = new();

        private Emitter(string moduleName, string[] references)
        {
            // Load provided references
            foreach (var reference in references)
            {
                try
                {
                    var assembly = AssemblyDefinition.ReadAssembly(reference);
                    _assemblies.Add(assembly);
                }
                catch (BadImageFormatException)
                {
                    _diagnostics.ReportInvalidReference(reference);
                }
            }

            // If no references were provided, load runtime assemblies automatically
            if (references.Length == 0)
            {
                LoadRuntimeAssemblies();
            }

            var assemblyName = new AssemblyNameDefinition(moduleName, new Version(1, 0));
            _assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ModuleKind.Console);

            _consoleWriteLineReference = ResolveMethod("System.Console", "WriteLine", new[] { "System.String" })!;
            _consoleReadLineReference = ResolveMethod("System.Console", "ReadLine", Array.Empty<string>())!;
            _stringConcatReference = ResolveMethod("System.String", "Concat", new[] { "System.Object", "System.Object" })!;
            _minReference = ResolveMethod("System.Math", "Min", new[] { "System.Int32", "System.Int32" })!;
            _maxReference = ResolveMethod("System.Math", "Max", new[] { "System.Int32", "System.Int32" })!;

            _listType = ResolveType("System.Collections.Generic.List`1")!;
            _dictionaryType = ResolveType("System.Collections.Generic.Dictionary`2")!;
        }

        private void LoadRuntimeAssemblies()
        {
            // Find reference assemblies in the SDK
            var sdkRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet", "packs", "Microsoft.NETCore.App.Ref");
            
            // Find the latest version
            var refAssembliesPath = Directory.GetDirectories(sdkRoot)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "ref", "net10.0"))
                .FirstOrDefault(Directory.Exists);

            if (refAssembliesPath == null)
            {
                // Fallback to runtime directory
                refAssembliesPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            }
            
            // Load essential .NET assemblies
            var requiredAssemblies = new[]
            {
                "System.Runtime.dll",
                "System.Console.dll",
                "System.Collections.dll",
                "System.Linq.dll",
                "Microsoft.CSharp.dll"
            };

            foreach (var assemblyName in requiredAssemblies)
            {
                var assemblyPath = Path.Combine(refAssembliesPath, assemblyName);
                if (File.Exists(assemblyPath))
                {
                    try
                    {
                        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = false });
                        _assemblies.Add(assembly);
                    }
                    catch (Exception)
                    {
                        // Ignore if we can't load the assembly
                    }
                }
            }
        }

        private TypeReference? ResolveType(string metaDataName)
        {
            var foundTypes = _assemblies.SelectMany(a => a.Modules)
                                        .SelectMany(a => a.Types)
                                        .Where(t => t.FullName == metaDataName)
                                        .ToArray();

            if (foundTypes.Length == 1)
            {
                return _assemblyDefinition.MainModule.ImportReference(foundTypes[0]);
            }
            else if (foundTypes.Length == 0)
            {
                _diagnostics.ReportRequiredTypeNotFound(null, metaDataName);
            }
            else
            {
                _diagnostics.ReportRequiredTypeAmbiguous(null, metaDataName, foundTypes);
            }

            return null;
        }

        private MethodReference? ResolveMethod(string typeName, string methodName, string[] parameterTypeNames)
        {
            var foundTypes = _assemblies.SelectMany(a => a.Modules)
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

        private TypeReference GetTypeReference(TypeSymbol type)
        {
            if (_knownTypes.TryGetValue(type, out var typeReference))
                return typeReference;

            TypeReference? resolved = null;

            if (type.TypeArguments.Length == 0)
            {
                resolved = type.Name switch
                {
                    "any" => ResolveType("System.Object"),
                    "bool" => ResolveType("System.Boolean"),
                    "int" => ResolveType("System.Int32"),
                    "string" => ResolveType("System.String"),
                    "void" => ResolveType("System.Void"),
                    "array" => _listType.MakeGenericInstanceType(ResolveType("System.Object")),
                    "map" => _dictionaryType.MakeGenericInstanceType(ResolveType("System.Object"), ResolveType("System.Object")),
                    _ => throw new Exception($"Unexpected type {type.Name}")
                };
            }
            else
            {
                if (type.Name == "array")
                {
                    var elementType = GetTypeReference(type.TypeArguments[0]);
                    resolved = _listType.MakeGenericInstanceType(elementType);
                }
                else if (type.Name == "map")
                {
                    var keyType = GetTypeReference(type.TypeArguments[0]);
                    var valueType = GetTypeReference(type.TypeArguments[1]);
                    resolved = _dictionaryType.MakeGenericInstanceType(keyType, valueType);
                }
            }

            if (resolved == null)
                throw new Exception($"Could not resolve type {type}");

            _knownTypes.Add(type, resolved);
            return resolved;
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

            var objectType = GetTypeReference(TypeSymbol.Any);

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
            var functionType = GetTypeReference(function.Type);

            var method = new MethodDefinition(function.Name, MethodAttributes.Static | MethodAttributes.Public, functionType);


            foreach (var parameter in function.Parameters)
            {
                var parameterType = GetTypeReference(parameter.Type);

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
            _labels.Clear();
            _fixups.Clear();

            var ilProcessor = method.Body.GetILProcessor();
            foreach (var statement in body.Statements)
            {
                EmitStatement(ilProcessor, statement);
            }

            if (method.ReturnType.FullName == "System.Void")
            {
                EmitInstruction(ilProcessor, OpCodes.Ret);
            }
            else if (function.Type == TypeSymbol.Any && method.ReturnType.IsValueType)
            {
                EmitInstruction(ilProcessor, OpCodes.Box, method.ReturnType);
                EmitInstruction(ilProcessor, OpCodes.Ret);
            }

            // Fixup goto instructions
            foreach (var (label, instruction) in _fixups)
            {
                if (!_labels.TryGetValue(label, out var target))
                {
                    throw new Exception($"Label {label.Name} not found");
                }
                instruction.Operand = target;
            }

            method.Body.OptimizeMacros();
        }

        private MethodReference GetGenericMethod(TypeReference type, string methodName, int parameterCount)
        {
            var typeDefinition = type.Resolve();
            var methodDefinition = typeDefinition.Methods.First(m => m.Name == methodName && m.Parameters.Count == parameterCount);

            var methodReference = _assemblyDefinition.MainModule.ImportReference(methodDefinition);

            if (type is GenericInstanceType genericType)
            {
                var specializedMethod = new MethodReference(methodReference.Name, methodReference.ReturnType, genericType);
                specializedMethod.HasThis = methodReference.HasThis;
                specializedMethod.ExplicitThis = methodReference.ExplicitThis;
                specializedMethod.CallingConvention = methodReference.CallingConvention;

                foreach (var parameter in methodReference.Parameters)
                {
                    specializedMethod.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                }

                return specializedMethod;
            }

            return methodReference;
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
            EmitExpression(ilProcessor, node.Condition);

            // Create a placeholder instruction - we'll fixup the target later
            var opCode = node.JumpIfTrue ? OpCodes.Brtrue : OpCodes.Brfalse;
            var instruction = ilProcessor.Create(opCode, Instruction.Create(OpCodes.Nop));
            ilProcessor.Append(instruction);
            _fixups.Add((node.BoundLabel, instruction));
        }

        private void EmitGotoStatement(ILProcessor ilProcessor, BoundGotoStatement node)
        {
            // Create a placeholder instruction - we'll fixup the target later
            var instruction = ilProcessor.Create(OpCodes.Br, Instruction.Create(OpCodes.Nop));
            ilProcessor.Append(instruction);
            _fixups.Add((node.BoundLabel, instruction));
        }

        private void EmitLabelStatement(ILProcessor ilProcessor, BoundLabelStatement node)
        {
            var instruction = ilProcessor.Create(OpCodes.Nop);
            ilProcessor.Append(instruction);
            _labels[node.BoundLabel] = instruction;
        }

        private void EmitVariableDeclaration(ILProcessor ilProcessor, BoundVariableDeclaration node)
        {
            var typeReference = GetTypeReference(node.Variable.Type);

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

            var collectionType = GetTypeReference(node.LHS.Type);
            var setMethod = GetGenericMethod(collectionType, "set_Item", 2);

            EmitInstruction(ilProcessor, OpCodes.Callvirt, setMethod);
            
            // Push a dummy value because the expression is expected to return something
            EmitInstruction(ilProcessor, OpCodes.Ldnull); 
        }

        private void EmitIndexExpression(ILProcessor ilProcessor, BoundIndexExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);
            EmitExpression(ilProcessor, node.Index);
            
            var collectionType = GetTypeReference(node.Expression.Type);
            var getMethod = GetGenericMethod(collectionType, "get_Item", 1);
            
            EmitInstruction(ilProcessor, OpCodes.Callvirt, getMethod);
        }

        private void EmitMapExpression(ILProcessor ilProcessor, BoundMapExpression node)
        {
            var mapType = GetTypeReference(node.Type);
            var constructor = GetGenericMethod(mapType, ".ctor", 0);
            var addMethod = GetGenericMethod(mapType, "Add", 2);

            EmitInstruction(ilProcessor, OpCodes.Newobj, constructor);
            foreach (var entry in node.Entries)
            {
                EmitInstruction(ilProcessor, OpCodes.Dup);
                EmitExpression(ilProcessor, entry.Key);
                EmitExpression(ilProcessor, entry.Value);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, addMethod);
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
            var arrayType = GetTypeReference(node.Type);
            var constructor = GetGenericMethod(arrayType, ".ctor", 0);
            var addMethod = GetGenericMethod(arrayType, "Add", 1);

            EmitInstruction(ilProcessor, OpCodes.Newobj, constructor);
            foreach (var element in node.Elements)
            {
                EmitInstruction(ilProcessor, OpCodes.Dup);
                EmitExpression(ilProcessor, element);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, addMethod);
                // List<T>.Add returns void, unlike ArrayList.Add which returns int.
                // So no need to pop here.
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
                    EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(fromType));
                }
                else if (toType == TypeSymbol.Int || toType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Unbox_Any, GetTypeReference(toType));
                }
            }
            else if (toType == TypeSymbol.String && (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool || fromType == TypeSymbol.Any))
            {
                if (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(fromType));
                }
                
                var toStringMethod = GetTypeReference(TypeSymbol.Any).Resolve().Methods.First(m => m.Name == "ToString" && m.Parameters.Count == 0);
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
            else if (node.Function == BuiltInFunctions.Push)
            {
                var listType = GetTypeReference(TypeSymbol.Array);
                _listAddMethod ??= GetGenericMethod(listType, "Add", 1);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _listAddMethod);
            }
            else if (node.Function == BuiltInFunctions.Pop)
            {
                var listType = GetTypeReference(TypeSymbol.Array);
                _listRemoveAtMethod ??= GetGenericMethod(listType, "RemoveAt", 1);
                _listGetCountMethod ??= GetGenericMethod(listType, "get_Count", 0);

                var local = new VariableDefinition(listType);
                ilProcessor.Body.Variables.Add(local);
                EmitInstruction(ilProcessor, OpCodes.Stloc, local);

                // get last element
                EmitInstruction(ilProcessor, OpCodes.Ldloc, local);
                EmitInstruction(ilProcessor, OpCodes.Ldloc, local);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _listGetCountMethod);
                EmitInstruction(ilProcessor, OpCodes.Ldc_I4_1);
                EmitInstruction(ilProcessor, OpCodes.Sub);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, GetGenericMethod(listType, "get_Item", 1));

                // remove last element
                EmitInstruction(ilProcessor, OpCodes.Ldloc, local);
                EmitInstruction(ilProcessor, OpCodes.Ldloc, local);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _listGetCountMethod);
                EmitInstruction(ilProcessor, OpCodes.Ldc_I4_1);
                EmitInstruction(ilProcessor, OpCodes.Sub);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _listRemoveAtMethod);
            }
            else if (node.Function == BuiltInFunctions.GetAt)
            {
                var listType = GetTypeReference(TypeSymbol.Array);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, GetGenericMethod(listType, "get_Item", 1));
            }
            else if (node.Function == BuiltInFunctions.Length)
            {
                var listType = GetTypeReference(TypeSymbol.Array);
                _listGetCountMethod ??= GetGenericMethod(listType, "get_Count", 0);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _listGetCountMethod);
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
                    EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(node.Left.Type));
                }
            }

            EmitExpression(ilProcessor, node.Right);
            if (node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String)
            {
                if (node.Right.Type != TypeSymbol.String && node.Right.Type != TypeSymbol.Any)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(node.Right.Type));
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
            else if (node.Op.Kind == BoundBinaryOperatorKind.Modulo)
            {
                EmitInstruction(ilProcessor, OpCodes.Rem);
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
