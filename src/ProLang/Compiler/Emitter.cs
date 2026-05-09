using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using ProLang.Intermediate;
using ProLang.Interop;
using ProLang.Parse;
using ProLang.Symbols;
using System.Collections.Immutable;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace ProLang.Compiler
{
    internal sealed class Emitter
    {
        private DiagnosticBag _diagnostics = new();

        private readonly Dictionary<TypeSymbol, TypeReference> _knownTypes = new();
        private readonly Dictionary<string, TypeReference> _typeCache = new();
        private readonly Dictionary<(TypeReference typeRef, string methodName, int paramCount), MethodReference> _methodCache = new();
        private readonly List<AssemblyDefinition> _assemblies = new();

        private readonly MethodReference _consoleReadLineReference;
        private readonly MethodReference _consoleWriteLineReference;
        private readonly MethodReference _stringConcatReference;
        private readonly MethodReference _minReference;
        private readonly MethodReference _maxReference;

        // Output infrastructure for centralized output collection
        private MethodReference? _outputInitMethod;
        private MethodReference? _outputAppendMethod;
        private MethodReference? _outputFlushMethod;
        private FieldDefinition? _outputField;
        private bool _outputInfrastructureGenerated = false;

        private MethodReference? _listAddMethod;
        private MethodReference? _listRemoveAtMethod;
        private MethodReference? _listGetCountMethod;

        private readonly TypeReference _listType;
        private readonly TypeReference _dictionaryType;

        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();

        private Dictionary<VariableSymbol, VariableDefinition> _locals = new();

        private Dictionary<StructSymbol, TypeDefinition> _structTypes = new();

        private TypeDefinition _typeDefinition;

        private Dictionary<BoundLabel, Instruction> _labels = new();
        private List<(BoundLabel label, Instruction instruction)> _fixups = new();

        private Emitter(string moduleName, string[] references)
        {
            // Always load runtime assemblies first to ensure core types are available
            LoadRuntimeAssemblies();

            // Load provided references
            foreach (var reference in references)
            {
                try
                {
                    var assembly = AssemblyDefinition.ReadAssembly(reference);
                    if (!_assemblies.Any(a => a.Name.Name == assembly.Name.Name))
                    {
                        _assemblies.Add(assembly);
                    }
                }
                catch (BadImageFormatException)
                {
                    _diagnostics.ReportInvalidReference(reference);
                }
            }

            var assemblyName = new AssemblyNameDefinition(moduleName, new Version(1, 0));
            _assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ModuleKind.Dll);

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
            string? assemblyLoadPath = null;

            // First, try to find the runtime assemblies in Program Files\dotnet\shared
            var programFilesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.NETCore.App");

            if (Directory.Exists(programFilesRoot))
            {
                // Find the latest version of the runtime
                var latestVersion = Directory.GetDirectories(programFilesRoot)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                if (latestVersion != null)
                {
                    assemblyLoadPath = latestVersion;
                }
            }

            // Second, try SDK packs
            if (assemblyLoadPath == null || !Directory.Exists(assemblyLoadPath))
            {
                var sdkRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "packs", "Microsoft.NETCore.App.Ref");

                if (Directory.Exists(sdkRoot))
                {
                    // Find the latest version
                    var latestVersion = Directory.GetDirectories(sdkRoot)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();
                    if (latestVersion != null)
                    {
                        var refDir = Path.Combine(latestVersion, "ref");
                        var framework = Directory.GetDirectories(refDir)
                            .OrderByDescending(d => d)
                            .FirstOrDefault();
                        if (framework != null)
                        {
                            assemblyLoadPath = framework;
                        }
                    }
                }
            }

            // Third, fallback to user profile
            if (assemblyLoadPath == null || !Directory.Exists(assemblyLoadPath))
            {
                var userRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".dotnet", "packs", "Microsoft.NETCore.App.Ref");

                if (Directory.Exists(userRoot))
                {
                    var latestVersion = Directory.GetDirectories(userRoot)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();
                    if (latestVersion != null)
                    {
                        var refDir = Path.Combine(latestVersion, "ref");
                        var framework = Directory.GetDirectories(refDir)
                            .OrderByDescending(d => d)
                            .FirstOrDefault();
                        if (framework != null)
                        {
                            assemblyLoadPath = framework;
                        }
                    }
                }
            }

            // Fourth, fallback to runtime directory
            if (assemblyLoadPath == null || !Directory.Exists(assemblyLoadPath))
            {
                assemblyLoadPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            }

            // Load essential .NET assemblies for interop
            // Note: System.Private.CoreLib must be loaded first as it contains the core types
            var requiredAssemblies = new[]
            {
                "System.Private.CoreLib.dll",  // Contains the actual type definitions
                "System.Runtime.dll",
                "System.Console.dll",
                "System.Collections.dll",
                "System.Collections.Concurrent.dll",
                "System.Linq.dll",
                "System.Text.RegularExpressions.dll",
                "System.IO.FileSystem.dll",
                "System.Threading.dll",
                "System.Net.Primitives.dll",
                "System.Text.Json.dll",
                "Microsoft.CSharp.dll"
            };

            var loadedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(assemblyLoadPath))
            {
                foreach (var assemblyName in requiredAssemblies)
                {
                    var assemblyPath = Path.Combine(assemblyLoadPath, assemblyName);
                    if (File.Exists(assemblyPath))
                    {
                        try
                        {
                            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = false });
                            var simpleName = assembly.Name.Name;
                            if (!loadedAssemblyNames.Contains(simpleName))
                            {
                                _assemblies.Add(assembly);
                                loadedAssemblyNames.Add(simpleName);
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore if we can't load the assembly
                        }
                    }
                }
            }
        }

        private TypeReference? ResolveType(string metaDataName)
        {
            // Check cache first
            if (_typeCache.TryGetValue(metaDataName, out var cached))
                return cached;

            TypeReference? result = null;
            TypeDefinition? foundType = null;
            var allFoundList = new List<TypeDefinition>();

            // Early termination: stop after finding first match
            foreach (var assembly in _assemblies)
            {
                foreach (var module in assembly.Modules)
                {
                    var typeInModule = module.Types.FirstOrDefault(t => t.FullName == metaDataName);
                    if (typeInModule != null)
                    {
                        if (foundType == null)
                        {
                            foundType = typeInModule;
                        }
                        else
                        {
                            // Collect multiple matches for error reporting
                            allFoundList.Add(typeInModule);
                        }
                    }
                }
            }

            if (foundType != null && allFoundList.Count == 0)
            {
                result = _assemblyDefinition.MainModule.ImportReference(foundType);
                _typeCache[metaDataName] = result;
            }
            else if (foundType == null)
            {
                _diagnostics.ReportRequiredTypeNotFound(null, metaDataName);
            }
            else
            {
                // Report ambiguous matches (foundType + items in allFoundList)
                var allTypes = new TypeDefinition[allFoundList.Count + 1];
                allTypes[0] = foundType;
                allFoundList.CopyTo(allTypes, 1);
                _diagnostics.ReportRequiredTypeAmbiguous(null, metaDataName, allTypes);
            }

            return result;
        }

        private MethodReference? ResolveMethod(string typeName, string methodName, string[] parameterTypeNames)
        {
            TypeDefinition? foundType = null;
            var allFoundTypesList = new List<TypeDefinition>();

            // Early termination: find type, stop at first match
            foreach (var assembly in _assemblies)
            {
                foreach (var module in assembly.Modules)
                {
                    var typeInModule = module.Types.FirstOrDefault(t => t.FullName == typeName);
                    if (typeInModule != null)
                    {
                        if (foundType == null)
                        {
                            foundType = typeInModule;
                        }
                        else
                        {
                            // Track multiple matches for error reporting
                            allFoundTypesList.Add(typeInModule);
                        }
                    }
                }
            }

            if (foundType != null && allFoundTypesList.Count == 0)
            {
                // Find matching method - avoid Where() allocation, use direct iteration
                foreach (var method in foundType.Methods)
                {
                    if (method.Name != methodName || method.Parameters.Count != parameterTypeNames.Length)
                        continue;

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
                        continue;

                    return _assemblyDefinition.MainModule.ImportReference(method);
                }

                _diagnostics.ReportRequiredMethodNotFound(typeName, methodName, parameterTypeNames);
                return null;
            }
            else if (foundType == null)
            {
                _diagnostics.ReportRequiredTypeNotFound(null, typeName);
            }
            else
            {
                // Report ambiguous matches
                var allTypes = new TypeDefinition[allFoundTypesList.Count + 1];
                allTypes[0] = foundType;
                allFoundTypesList.CopyTo(allTypes, 1);
                _diagnostics.ReportRequiredTypeAmbiguous(null, typeName, allTypes);
            }

            return null;
        }

        private TypeReference GetTypeReference(TypeSymbol type)
        {
            if (_knownTypes.TryGetValue(type, out var typeReference))
                return typeReference;

            if (type is StructSymbol structType && _structTypes.TryGetValue(structType, out var structTypeDef))
            {
                var structTypeRef = _assemblyDefinition.MainModule.ImportReference(structTypeDef);
                _knownTypes.Add(type, structTypeRef);
                return structTypeRef;
            }

            TypeReference? resolved = null;

            if (type.TypeArguments.Length == 0)
            {
                resolved = type.Name switch
                {
                    "any" => GetCachedType("System.Object"),
                    "bool" => GetCachedType("System.Boolean"),
                    "int" => GetCachedType("System.Int32"),
                    "string" => GetCachedType("System.String"),
                    "void" => GetCachedType("System.Void"),
                    "array" => _listType.MakeGenericInstanceType(GetCachedType("System.Object")),
                    "map" => _dictionaryType.MakeGenericInstanceType(GetCachedType("System.Object"), GetCachedType("System.Object")),
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

        private TypeReference GetCachedType(string metaDataName)
        {
            if (_typeCache.TryGetValue(metaDataName, out var cached))
                return cached;

            var resolved = ResolveType(metaDataName);
            if (resolved == null)
                throw new Exception($"Could not resolve required type {metaDataName}");

            return resolved;
        }

        private void EmitStructType(StructSymbol structSymbol)
        {
            var typeDef = new TypeDefinition(
                "",
                structSymbol.Name,
                Mono.Cecil.TypeAttributes.SequentialLayout | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.Public,
                ResolveType("System.ValueType")
            );

            foreach (var field in structSymbol.Fields)
            {
                var fieldType = GetTypeReference(field.Type);
                var fieldDef = new FieldDefinition(
                    field.Name,
                    Mono.Cecil.FieldAttributes.Public,
                    fieldType
                );
                typeDef.Fields.Add(fieldDef);
            }

            _assemblyDefinition.MainModule.Types.Add(typeDef);
            _structTypes.Add(structSymbol, typeDef);
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
            _typeDefinition = new TypeDefinition("", "Program", CecilTypeAttributes.Abstract | CecilTypeAttributes.Sealed, objectType);

            _assemblyDefinition.MainModule.Types.Add(_typeDefinition);

            // Emit output collection infrastructure for all assemblies
            // (libraries may have functions that use print())
            EmitOutputHelpers();

            foreach (var structType in program.StructTypes)
            {
                EmitStructType(structType);
            }

            foreach (var functionWithBody in program.Functions)
            {
                // Skip synthetic functions - they will be emitted separately
                if (functionWithBody.Key.Declaration != null)
                {
                    EmitFunctionDeclaration(functionWithBody.Key);
                }
            }

            foreach (var functionWithBody in program.Functions)
            {
                // Skip synthetic functions - they will be emitted separately
                if (functionWithBody.Key.Declaration != null)
                {
                    EmitFunctionBody(functionWithBody.Key, functionWithBody.Value);
                }
            }

            // Emit synthetic __Main entry point if we have a main function
            if (program.MainFunction != null && program.MainFunction.Name == "__Main")
            {
                EmitSyntheticMainMethod(program);
            }

            if (program.MainFunction != null)
            {
                _assemblyDefinition.EntryPoint = _methods[program.MainFunction];
            }

            _assemblyDefinition.Write(outputPath);

            // Generate .runtimeconfig.json for running with dotnet
            var runtimeConfigPath = Path.ChangeExtension(outputPath, ".runtimeconfig.json");
            if (runtimeConfigPath != null)
            {
                var runtimeConfig = """
                    {
                      "runtimeOptions": {
                        "tfm": "net10.0",
                        "framework": {
                          "name": "Microsoft.NETCore.App",
                          "version": "10.0.0"
                        }
                      }
                    }
                    """;
                File.WriteAllText(runtimeConfigPath, runtimeConfig);
            }

            return _diagnostics.ToImmutableArray();
        }

        private void EmitOutputHelpers()
        {
            if (_outputInfrastructureGenerated)
                return;

            // Create the static StringBuilder field to hold accumulated output
            var stringBuilderType = ResolveType("System.Text.StringBuilder");
            _outputField = new FieldDefinition("__output",
                FieldAttributes.Static | FieldAttributes.Private,
                stringBuilderType);
            _typeDefinition.Fields.Add(_outputField);

            // Resolve necessary types and methods
            var voidType = GetTypeReference(TypeSymbol.Void);
            var stringType = GetTypeReference(TypeSymbol.String);

            // Create __InitializeOutput() method
            var initMethod = new MethodDefinition("__InitializeOutput",
                CecilMethodAttributes.Static | CecilMethodAttributes.Private,
                voidType);
            var initIL = initMethod.Body.GetILProcessor();

            // IL: __output = new StringBuilder();
            var sbConstructor = ResolveMethod("System.Text.StringBuilder", ".ctor", Array.Empty<string>());
            EmitInstruction(initIL, OpCodes.Newobj, sbConstructor);
            EmitInstruction(initIL, OpCodes.Stsfld, _outputField);
            EmitInstruction(initIL, OpCodes.Ret);

            initMethod.Body.OptimizeMacros();
            _typeDefinition.Methods.Add(initMethod);
            _outputInitMethod = initMethod;

            // Create __AppendToOutput(string value) method
            var appendMethod = new MethodDefinition("__AppendToOutput",
                CecilMethodAttributes.Static | CecilMethodAttributes.Private,
                voidType);
            appendMethod.Parameters.Add(new ParameterDefinition("value",
                CecilParameterAttributes.None,
                stringType));

            var appendIL = appendMethod.Body.GetILProcessor();

            // IL: __output.AppendLine(value);
            // This preserves the behavior of Console.WriteLine() which adds a newline
            var sbAppendLineMethod = ResolveMethod("System.Text.StringBuilder", "AppendLine", new[] { "System.String" });
            EmitInstruction(appendIL, OpCodes.Ldsfld, _outputField);
            EmitInstruction(appendIL, OpCodes.Ldarg_0);  // Load the value parameter
            EmitInstruction(appendIL, OpCodes.Callvirt, sbAppendLineMethod);
            EmitInstruction(appendIL, OpCodes.Pop);  // Pop the StringBuilder return value
            EmitInstruction(appendIL, OpCodes.Ret);

            appendMethod.Body.OptimizeMacros();
            _typeDefinition.Methods.Add(appendMethod);
            _outputAppendMethod = appendMethod;

            // Create __FlushOutput() method
            var flushMethod = new MethodDefinition("__FlushOutput",
                CecilMethodAttributes.Static | CecilMethodAttributes.Private,
                voidType);

            var flushIL = flushMethod.Body.GetILProcessor();

            // IL: Console.WriteLine(__output.ToString());
            var toStringMethod = ResolveMethod("System.Text.StringBuilder", "ToString", Array.Empty<string>());
            EmitInstruction(flushIL, OpCodes.Ldsfld, _outputField);
            EmitInstruction(flushIL, OpCodes.Callvirt, toStringMethod);
            EmitInstruction(flushIL, OpCodes.Call, _consoleWriteLineReference);
            EmitInstruction(flushIL, OpCodes.Ret);

            flushMethod.Body.OptimizeMacros();
            _typeDefinition.Methods.Add(flushMethod);
            _outputFlushMethod = flushMethod;

            _outputInfrastructureGenerated = true;
        }

        private void EmitSyntheticMainMethod(BoundProgram program)
        {
            // Find the __UserMain function
            var userMainFunction = program.Functions.Keys.FirstOrDefault(f => f.Name == "__UserMain");
            if (userMainFunction == null)
                return;

            var voidType = GetTypeReference(TypeSymbol.Void);
            var stringType = ResolveType("System.String");
            var stringArrayType = stringType?.MakeArrayType();

            // Create __Main(string[] args) method
            var mainMethod = new MethodDefinition("__Main",
                CecilMethodAttributes.Static | CecilMethodAttributes.Public,
                voidType);

            // Add string[] args parameter
            mainMethod.Parameters.Add(new ParameterDefinition("args",
                CecilParameterAttributes.None,
                stringArrayType));

            var ilProcessor = mainMethod.Body.GetILProcessor();

            // 1. Call __InitializeOutput()
            EmitInstruction(ilProcessor, OpCodes.Call, _outputInitMethod);

            // 2. Prepare to call __UserMain
            // If __UserMain expects args, pass the string[] directly
            // If __UserMain takes no args, don't pass anything

            if (userMainFunction.Parameters.Any())
            {
                // __UserMain expects array<string> parameter - pass args directly
                // (string[] from CLR maps directly to array<string> in ProLang IL)
                EmitInstruction(ilProcessor, OpCodes.Ldarg_0);  // Load args parameter
                EmitInstruction(ilProcessor, OpCodes.Call, _methods[userMainFunction]);
            }
            else
            {
                // __UserMain takes no parameters - just call it
                EmitInstruction(ilProcessor, OpCodes.Call, _methods[userMainFunction]);
            }

            // 3. Call __FlushOutput() to print accumulated output
            EmitInstruction(ilProcessor, OpCodes.Call, _outputFlushMethod);

            // 4. Return void
            EmitInstruction(ilProcessor, OpCodes.Ret);

            mainMethod.Body.OptimizeMacros();
            _typeDefinition.Methods.Add(mainMethod);
            _methods[program.MainFunction] = mainMethod;
        }

        private void EmitFunctionDeclaration(FunctionSymbol function)
        {
            var functionType = GetTypeReference(function.Type);

            var method = new MethodDefinition(function.Name, CecilMethodAttributes.Static | CecilMethodAttributes.Public, functionType);


            foreach (var parameter in function.Parameters)
            {
                var parameterType = GetTypeReference(parameter.Type);

                var parameterAttributes = CecilParameterAttributes.None;

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
            // Check cache first
            var cacheKey = (type, methodName, parameterCount);
            if (_methodCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var typeDefinition = type.Resolve();
            var methodDefinition = typeDefinition.Methods.First(m => m.Name == methodName && m.Parameters.Count == parameterCount);

            var methodReference = _assemblyDefinition.MainModule.ImportReference(methodDefinition);

            if (type is GenericInstanceType genericType)
            {
                var specializedMethod = new MethodReference(methodReference.Name, methodReference.ReturnType, genericType)
                {
                    HasThis = methodReference.HasThis,
                    ExplicitThis = methodReference.ExplicitThis,
                    CallingConvention = methodReference.CallingConvention
                };

                foreach (var parameter in methodReference.Parameters)
                {
                    specializedMethod.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                }

                _methodCache[cacheKey] = specializedMethod;
                return specializedMethod;
            }

            _methodCache[cacheKey] = methodReference;
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

            if (node.Variable.Type is StructSymbol)
            {
                EmitInstruction(ilProcessor, OpCodes.Ldloca, variableDefinition);
                EmitInstruction(ilProcessor, OpCodes.Initobj, typeReference);
            }

            EmitExpression(ilProcessor, node.Initializer);

            if (node.Variable.Type == TypeSymbol.Any && typeReference.IsValueType)
            {
                EmitInstruction(ilProcessor, OpCodes.Box, typeReference);
            }

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
                case BoundNodeKind.BoundStructCreationExpression:
                    EmitStructCreationExpression(ilProcessor, (BoundStructCreationExpression)node);
                    break;
                case BoundNodeKind.BoundFieldAccessExpression:
                    EmitFieldAccessExpression(ilProcessor, (BoundFieldAccessExpression)node);
                    break;
                case BoundNodeKind.BoundFieldAssignmentExpression:
                    EmitFieldAssignmentExpression(ilProcessor, (BoundFieldAssignmentExpression)node);
                    break;
                case BoundNodeKind.BoundCastExpression:
                    EmitCastExpression(ilProcessor, (BoundCastExpression)node);
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

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, ParameterDefinition parameter)
        {
            ilProcessor.Emit(opCode, parameter);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, int value)
        {
            ilProcessor.Emit(opCode, value);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, string value)
        {
            ilProcessor.Emit(opCode, value);
        }

        private void EmitInstruction(ILProcessor ilProcessor, OpCode opCode, FieldReference field)
        {
            ilProcessor.Emit(opCode, field);
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

            if (toType == TypeSymbol.String && (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool || fromType == TypeSymbol.Any))
            {
                if (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(fromType));
                }

                var toStringMethod = GetTypeReference(TypeSymbol.Any).Resolve().Methods.First(m => m.Name == "ToString" && m.Parameters.Count == 0);
                EmitInstruction(ilProcessor, OpCodes.Callvirt, _assemblyDefinition.MainModule.ImportReference(toStringMethod));
            }
            else if (fromType == TypeSymbol.Any || toType == TypeSymbol.Any)
            {
                // handle boxing/unboxing between primitives and any
                if (fromType == TypeSymbol.Int || fromType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(fromType));
                }
                else if (toType == TypeSymbol.Int || toType == TypeSymbol.Bool)
                {
                    EmitInstruction(ilProcessor, OpCodes.Unbox_Any, GetTypeReference(toType));
                }
            }
        }

        private void EmitCastExpression(ILProcessor ilProcessor, BoundCastExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);

            var targetType = node.TargetType;

            // Safe casting from 'any' type to target type
            if (targetType == TypeSymbol.Int || targetType == TypeSymbol.Bool)
            {
                // Unbox from object to value type - throws InvalidCastException if type mismatch
                EmitInstruction(ilProcessor, OpCodes.Unbox_Any, GetTypeReference(targetType));
            }
            else if (targetType == TypeSymbol.String)
            {
                // Cast to string - value is already object
                EmitInstruction(ilProcessor, OpCodes.Isinst, GetTypeReference(targetType));
            }
            else if (targetType.Name == "array" || targetType.Name == "map")
            {
                // For collection types, just cast with isinst (reference types)
                EmitInstruction(ilProcessor, OpCodes.Isinst, GetTypeReference(targetType));
            }
            else
            {
                // For other types (structs, etc.), use isinst
                EmitInstruction(ilProcessor, OpCodes.Isinst, GetTypeReference(targetType));
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
                // Route print() through output collection infrastructure
                EmitInstruction(ilProcessor, OpCodes.Call, _outputAppendMethod);
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
            else if (node.Function == BuiltInFunctions.StringLength)
            {
                var lengthMethod = ResolveMethod("System.String", "get_Length", Array.Empty<string>());
                if (lengthMethod != null)
                {
                    EmitInstruction(ilProcessor, OpCodes.Callvirt, lengthMethod);
                }
            }
            else if (node.Function == BuiltInFunctions.StringCharAt)
            {
                var charAtMethod = ResolveMethod("System.String", "get_Chars", new[] { "System.Int32" });
                if (charAtMethod != null)
                {
                    EmitInstruction(ilProcessor, OpCodes.Callvirt, charAtMethod);

                    // Convert char to string
                    // We need to call string.Create or use a static method to convert char to string
                    // For now, use char.ToString() which should work
                    var charType = ResolveType("System.Char");
                    if (charType != null)
                    {
                        // Call the ToString method on the char value (non-virtual call for value types)
                        var toStringMethod = ResolveMethod("System.Char", "ToString", Array.Empty<string>());
                        if (toStringMethod != null)
                        {
                            // For value types, we need to use Call, not Callvirt
                            // But first we need to use the proper overload or convert differently
                            // Use string.Create or string.Concat approach
                            // Actually, let's use System.Convert.ToString(object)
                            var convertMethod = ResolveMethod("System.Convert", "ToString", new[] { "System.Object" });
                            if (convertMethod != null)
                            {
                                // Box the char first
                                EmitInstruction(ilProcessor, OpCodes.Box, charType);
                                EmitInstruction(ilProcessor, OpCodes.Call, convertMethod);
                            }
                            else
                            {
                                // Fallback: just call ToString on the char
                                EmitInstruction(ilProcessor, OpCodes.Call, toStringMethod);
                            }
                        }
                    }
                }
            }
            else if (node.Function == BuiltInFunctions.StringSubstring)
            {
                var substringMethod = ResolveMethod("System.String", "Substring", new[] { "System.Int32", "System.Int32" });
                if (substringMethod != null)
                {
                    // Stack: [string, start, end]  (ProLang uses end-exclusive index)
                    // .NET Substring(start, length) needs length = end - start
                    var intType = GetTypeReference(TypeSymbol.Int);
                    var tempEnd = new VariableDefinition(intType);
                    var tempStart = new VariableDefinition(intType);
                    ilProcessor.Body.Variables.Add(tempEnd);
                    ilProcessor.Body.Variables.Add(tempStart);
                    EmitInstruction(ilProcessor, OpCodes.Stloc, tempEnd);
                    EmitInstruction(ilProcessor, OpCodes.Stloc, tempStart);
                    EmitInstruction(ilProcessor, OpCodes.Ldloc, tempStart);
                    EmitInstruction(ilProcessor, OpCodes.Ldloc, tempEnd);
                    EmitInstruction(ilProcessor, OpCodes.Ldloc, tempStart);
                    EmitInstruction(ilProcessor, OpCodes.Sub);
                    EmitInstruction(ilProcessor, OpCodes.Callvirt, substringMethod);
                }
            }
            else if (node.Function == BuiltInFunctions.StringIndexOf)
            {
                var indexOfMethod = ResolveMethod("System.String", "IndexOf", new[] { "System.String" });
                if (indexOfMethod != null)
                {
                    EmitInstruction(ilProcessor, OpCodes.Callvirt, indexOfMethod);
                }
            }
            else if (node.Function == BuiltInFunctions.FileExists)
            {
                var method = ResolveMethod("System.IO.File", "Exists", new[] { "System.String" });
                if (method != null)
                    EmitInstruction(ilProcessor, OpCodes.Call, method);
            }
            else if (node.Function == BuiltInFunctions.ReadFile)
            {
                var method = ResolveMethod("System.IO.File", "ReadAllText", new[] { "System.String" });
                if (method != null)
                    EmitInstruction(ilProcessor, OpCodes.Call, method);
            }
            else if (node.Function == BuiltInFunctions.WriteFile)
            {
                var method = ResolveMethod("System.IO.File", "WriteAllText", new[] { "System.String", "System.String" });
                if (method != null)
                    EmitInstruction(ilProcessor, OpCodes.Call, method);
            }
            else if (node.Function is DotNetFunctionSymbol dotNetFunc)
            {
                EmitDotNetCallExpression(ilProcessor, dotNetFunc);
            }
            else
            {
                var methodDefinition = _methods[node.Function];

                EmitInstruction(ilProcessor, OpCodes.Call, methodDefinition);
            }
        }

        /// <summary>
        /// Emits a .NET method call instruction.
        /// </summary>
        private void EmitDotNetCallExpression(ILProcessor ilProcessor, DotNetFunctionSymbol dotNetFunc)
        {
            if (dotNetFunc.ConstructorInfo != null)
            {
                // Resolve and emit constructor call
                var typeRef = ResolveDotNetType(dotNetFunc.DeclaringType);
                var methodRef = ResolveDotNetConstructor(dotNetFunc.ConstructorInfo, typeRef);
                EmitInstruction(ilProcessor, OpCodes.Newobj, methodRef);
                
                // Box value types if the return type is Any (object)
                if (dotNetFunc.Type == TypeSymbol.Any && dotNetFunc.DeclaringType.IsValueType)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, typeRef);
                }
                return;
            }

            if (dotNetFunc.MethodInfo != null)
            {
                var typeRef = ResolveDotNetType(dotNetFunc.DeclaringType);
                var methodRef = ResolveDotNetMethod(dotNetFunc.MethodInfo, typeRef);

                if (dotNetFunc.IsStatic)
                {
                    EmitInstruction(ilProcessor, OpCodes.Call, methodRef);
                }
                else
                {
                    EmitInstruction(ilProcessor, OpCodes.Callvirt, methodRef);
                }
                
                // Box value types if the return type is Any (object)
                if (dotNetFunc.Type == TypeSymbol.Any && dotNetFunc.MethodInfo.ReturnType.IsValueType)
                {
                    var returnTypeRef = ResolveDotNetType(dotNetFunc.MethodInfo.ReturnType);
                    EmitInstruction(ilProcessor, OpCodes.Box, returnTypeRef);
                }
                return;
            }

            // Extract actual member name from qualified name (e.g., "Math.PI" -> "PI")
            var memberName = dotNetFunc.Name.Contains('.')
                ? dotNetFunc.Name.Substring(dotNetFunc.Name.LastIndexOf('.') + 1)
                : dotNetFunc.Name;

            // Static field/property access
            var field = dotNetFunc.DeclaringType.GetField(memberName);
            if (field != null)
            {
                var typeRef = ResolveDotNetType(dotNetFunc.DeclaringType);
                var fieldRef = new FieldReference(field.Name, ResolveDotNetTypeAsTypeRef(field.FieldType), typeRef);
                EmitInstruction(ilProcessor, OpCodes.Ldsfld, _assemblyDefinition.MainModule.ImportReference(fieldRef));
                
                // Box value types if the return type is Any (object)
                if (dotNetFunc.Type == TypeSymbol.Any && field.FieldType.IsValueType)
                {
                    var fieldTypeRef = ResolveDotNetType(field.FieldType);
                    EmitInstruction(ilProcessor, OpCodes.Box, fieldTypeRef);
                }
                return;
            }

            var property = dotNetFunc.DeclaringType.GetProperty(memberName);
            if (property?.GetMethod != null)
            {
                var typeRef = ResolveDotNetType(dotNetFunc.DeclaringType);
                var methodRef = ResolveDotNetMethod(property.GetMethod, typeRef);
                EmitInstruction(ilProcessor, OpCodes.Call, methodRef);
                
                // Box value types if the return type is Any (object)
                if (dotNetFunc.Type == TypeSymbol.Any && property.PropertyType.IsValueType)
                {
                    var propTypeRef = ResolveDotNetType(property.PropertyType);
                    EmitInstruction(ilProcessor, OpCodes.Box, propTypeRef);
                }
                return;
            }

            throw new NotSupportedException($"Cannot emit .NET member '{dotNetFunc.Name}' (member: '{memberName}')");
        }

        /// <summary>
        /// Resolves a .NET type to a Mono.Cecil TypeReference.
        /// </summary>
        private TypeReference ResolveDotNetType(Type type)
        {
            // Try to find in loaded assemblies
            var typeRef = _assemblies
                .SelectMany(a => a.Modules)
                .SelectMany(m => m.Types)
                .FirstOrDefault(t => t.FullName == type.FullName);

            if (typeRef != null)
            {
                return _assemblyDefinition.MainModule.ImportReference(typeRef);
            }

            // Try to resolve from runtime
            try
            {
                var assemblyLocation = type.Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation) && File.Exists(assemblyLocation))
                {
                    var runtimeAssembly = AssemblyDefinition.ReadAssembly(assemblyLocation);
                    _assemblies.Add(runtimeAssembly);
                    
                    typeRef = runtimeAssembly.MainModule.Types.FirstOrDefault(t => t.FullName == type.FullName);
                    if (typeRef != null)
                    {
                        return _assemblyDefinition.MainModule.ImportReference(typeRef);
                    }
                }
            }
            catch
            {
                // Ignore
            }

            throw new TypeLoadException($"Cannot resolve .NET type '{type.FullName}' in loaded assemblies");
        }

        /// <summary>
        /// Resolves a .NET method to a Mono.Cecil MethodReference.
        /// </summary>
        private MethodReference ResolveDotNetMethod(System.Reflection.MethodInfo method, TypeReference typeRef)
        {
            // Find the TypeDefinition from loaded assemblies (avoid using typeRef.Resolve() which uses Cecil's resolver)
            TypeDefinition? typeDef = _assemblies
                .SelectMany(a => a.Modules)
                .SelectMany(m => m.Types)
                .FirstOrDefault(t => t.FullName == typeRef.FullName);

            if (typeDef == null)
            {
                throw new TypeLoadException($"Cannot resolve type '{typeRef.FullName}' in loaded assemblies");
            }

            // Search for the method on the type and its base types
            var currentTypeDef = typeDef;
            while (currentTypeDef != null)
            {
                var methodDef = currentTypeDef.Methods.FirstOrDefault(m =>
                    m.Name == method.Name &&
                    m.Parameters.Count == method.GetParameters().Length);

                if (methodDef != null)
                {
                    // Create a proper method reference
                    var methodRef = _assemblyDefinition.MainModule.ImportReference(methodDef);
                    
                    // If the type is a generic instance, we need to make the method reference generic too
                    if (typeRef is GenericInstanceType genericType)
                    {
                        var specializedMethod = new MethodReference(methodDef.Name, methodDef.ReturnType, genericType);
                        specializedMethod.HasThis = methodDef.HasThis;
                        specializedMethod.ExplicitThis = methodDef.ExplicitThis;
                        specializedMethod.CallingConvention = methodDef.CallingConvention;
                        
                        foreach (var param in methodDef.Parameters)
                        {
                            specializedMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));
                        }
                        
                        return _assemblyDefinition.MainModule.ImportReference(specializedMethod);
                    }
                    
                    return methodRef;
                }

                // Check base type - search in loaded assemblies
                if (currentTypeDef.BaseType != null)
                {
                    currentTypeDef = _assemblies
                        .SelectMany(a => a.Modules)
                        .SelectMany(m => m.Types)
                        .FirstOrDefault(t => t.FullName == currentTypeDef.BaseType.FullName);
                }
                else
                {
                    break;
                }
            }

            throw new MissingMethodException($"Cannot resolve method '{method.Name}' on type '{typeRef.FullName}'");
        }

        /// <summary>
        /// Resolves a .NET constructor to a Mono.Cecil MethodReference.
        /// </summary>
        private MethodReference ResolveDotNetConstructor(System.Reflection.ConstructorInfo constructor, TypeReference typeRef)
        {
            var typeDef = typeRef.Resolve();
            var ctorDef = typeDef.Methods.FirstOrDefault(m =>
                m.IsConstructor &&
                m.Parameters.Count == constructor.GetParameters().Length);

            if (ctorDef != null)
            {
                return _assemblyDefinition.MainModule.ImportReference(ctorDef);
            }

            throw new MissingMethodException($"Cannot resolve constructor on type '{typeRef.FullName}'");
        }

        /// <summary>
        /// Resolves a .NET type to a TypeReference for field type resolution.
        /// </summary>
        private TypeReference ResolveDotNetTypeAsTypeRef(Type type)
        {
            return type.FullName switch
            {
                "System.Void" => ResolveType("System.Void")!,
                "System.Boolean" => ResolveType("System.Boolean")!,
                "System.Int32" => ResolveType("System.Int32")!,
                "System.String" => ResolveType("System.String")!,
                "System.Object" => ResolveType("System.Object")!,
                _ => ResolveDotNetType(type)
            };
        }

        private void EmitBinaryExpression(ILProcessor ilProcessor, BoundBinaryExpression node)
        {
            EmitExpression(ilProcessor, node.Left);
            if (node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String)
            {
                // Box non-string types for String.Concat(object, object)
                // For Any type (which could be a value type like DateTime), we need to box too
                if (node.Left.Type != TypeSymbol.String)
                {
                    if (node.Left.Type == TypeSymbol.Any || node.Left.Type == TypeSymbol.Int || node.Left.Type == TypeSymbol.Bool)
                    {
                        EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(node.Left.Type));
                    }
                }
            }

            EmitExpression(ilProcessor, node.Right);
            if (node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String)
            {
                // Box non-string types for String.Concat(object, object)
                // For Any type (which could be a value type like DateTime), we need to box too
                if (node.Right.Type != TypeSymbol.String)
                {
                    if (node.Right.Type == TypeSymbol.Any || node.Right.Type == TypeSymbol.Int || node.Right.Type == TypeSymbol.Bool)
                    {
                        EmitInstruction(ilProcessor, OpCodes.Box, GetTypeReference(node.Right.Type));
                    }
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
                if (node.Left.Type == TypeSymbol.String || node.Right.Type == TypeSymbol.String)
                {
                    var eqMethod = ResolveMethod("System.String", "op_Equality", new[] { "System.String", "System.String" });
                    if (eqMethod != null)
                        EmitInstruction(ilProcessor, OpCodes.Call, eqMethod);
                    else
                        EmitInstruction(ilProcessor, OpCodes.Ceq);
                }
                else
                {
                    EmitInstruction(ilProcessor, OpCodes.Ceq);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.NotEquals)
            {
                if (node.Left.Type == TypeSymbol.String || node.Right.Type == TypeSymbol.String)
                {
                    var neqMethod = ResolveMethod("System.String", "op_Inequality", new[] { "System.String", "System.String" });
                    if (neqMethod != null)
                        EmitInstruction(ilProcessor, OpCodes.Call, neqMethod);
                    else
                    {
                        EmitInstruction(ilProcessor, OpCodes.Ceq);
                        EmitInstruction(ilProcessor, OpCodes.Ldc_I4_0);
                        EmitInstruction(ilProcessor, OpCodes.Ceq);
                    }
                }
                else
                {
                    EmitInstruction(ilProcessor, OpCodes.Ceq);
                    EmitInstruction(ilProcessor, OpCodes.Ldc_I4_0);
                    EmitInstruction(ilProcessor, OpCodes.Ceq);
                }
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
            EmitExpression(ilProcessor, node.Expression);
            EmitInstruction(ilProcessor, OpCodes.Dup);

            if (node.Variable is ParameterSymbol parameter)
            {
                EmitInstruction(ilProcessor, OpCodes.Starg, ilProcessor.Body.Method.Parameters[parameter.Ordinal]);
            }
            else
            {
                var variableDefinition = _locals[node.Variable];
                EmitInstruction(ilProcessor, OpCodes.Stloc, variableDefinition);
            }
        }

        private void EmitVariableExpression(ILProcessor ilProcessor, BoundVariableExpression node)
        {
            if (node.Variable is ParameterSymbol parameter)
            {
                EmitInstruction(ilProcessor, OpCodes.Ldarg, ilProcessor.Body.Method.Parameters[parameter.Ordinal]);
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

        private void EmitStructCreationExpression(ILProcessor ilProcessor, BoundStructCreationExpression node)
        {
            var structSymbol = node.StructType;
            var typeDef = _structTypes[structSymbol];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);

            var localVar = new VariableDefinition(typeRef);
            ilProcessor.Body.Variables.Add(localVar);

            foreach (var field in structSymbol.Fields)
            {
                EmitInstruction(ilProcessor, OpCodes.Ldloca, localVar);

                var fieldIndex = structSymbol.Fields.IndexOf(field);
                EmitExpression(ilProcessor, node.FieldValues[fieldIndex]);

                var fieldType = GetTypeReference(field.Type);
                if (field.Type != TypeSymbol.Any && !fieldType.IsValueType)
                {
                    EmitInstruction(ilProcessor, OpCodes.Box, fieldType);
                }

                var fieldRef = new FieldReference(field.Name, fieldType);
                fieldRef.DeclaringType = typeRef;
                EmitInstruction(ilProcessor, OpCodes.Stfld, fieldRef);
            }

            EmitInstruction(ilProcessor, OpCodes.Ldloc, localVar);
        }

        private void EmitFieldAccessExpression(ILProcessor ilProcessor, BoundFieldAccessExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);

            var structSymbol = (StructSymbol)node.Expression.Type;
            var typeDef = _structTypes[structSymbol];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);
            var fieldType = GetTypeReference(node.Field.Type);
            var fieldRef = new FieldReference(node.FieldName, fieldType);
            fieldRef.DeclaringType = typeRef;

            EmitInstruction(ilProcessor, OpCodes.Ldfld, fieldRef);

            if (node.Type == TypeSymbol.Any && fieldType.IsValueType)
            {
                EmitInstruction(ilProcessor, OpCodes.Box, fieldType);
            }
        }

        private void EmitFieldAssignmentExpression(ILProcessor ilProcessor, BoundFieldAssignmentExpression node)
        {
            var structSymbol = (StructSymbol)node.Expression.Type;
            var typeDef = _structTypes[structSymbol];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);
            var fieldType = GetTypeReference(node.Field.Type);
            var fieldRef = new FieldReference(node.FieldName, fieldType);
            fieldRef.DeclaringType = typeRef;

            if (node.Expression is BoundVariableExpression varExpr)
            {
                VariableDefinition? varDef = null;
                if (varExpr.Variable is ParameterSymbol paramSym)
                {
                    EmitInstruction(ilProcessor, OpCodes.Ldarga, ilProcessor.Body.Method.Parameters[paramSym.Ordinal]);
                }
                else
                {
                    varDef = _locals[varExpr.Variable];
                    EmitInstruction(ilProcessor, OpCodes.Ldloca, varDef);
                }

                EmitExpression(ilProcessor, node.Value);
                EmitInstruction(ilProcessor, OpCodes.Dup);

                var tempVar = new VariableDefinition(fieldType);
                ilProcessor.Body.Variables.Add(tempVar);
                EmitInstruction(ilProcessor, OpCodes.Stloc, tempVar);

                EmitInstruction(ilProcessor, OpCodes.Stfld, fieldRef);

                EmitInstruction(ilProcessor, OpCodes.Ldloc, tempVar);
            }
            else
            {
                EmitExpression(ilProcessor, node.Expression);
                EmitInstruction(ilProcessor, OpCodes.Dup);
                EmitExpression(ilProcessor, node.Value);
                EmitInstruction(ilProcessor, OpCodes.Stfld, fieldRef);
            }
        }
    }
}
