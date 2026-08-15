using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using ProLang.CodeGen.DotNet;
using ProLang.CodeGen.DotNet.Intrinsics;
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

        /// <summary>Resolves BCL and referenced-assembly members into the emitted module.</summary>
        private readonly ReferenceResolver _references;

        /// <summary>Emits calls into .NET assemblies the program imported.</summary>
        private readonly InteropEmitter _interop;

        private readonly MethodReference _stringConcatReference;

        // Entry points into ProLang.Runtime.Output, which buffers print() and flushes at exit.
        private MethodReference? _outputInitMethod;
        private MethodReference? _outputAppendMethod;
        private MethodReference? _outputFlushMethod;
        private bool _outputInfrastructureGenerated = false;

        private readonly TypeReference _dictionaryType;

        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();

        private Dictionary<VariableSymbol, VariableDefinition> _locals = new();

        // Keyed by struct name so concrete instantiations (DynArray<int>) are deduplicated by name.
        private Dictionary<string, TypeDefinition> _structTypes = new(StringComparer.Ordinal);

        private TypeDefinition _typeDefinition;

        private Dictionary<BoundLabel, Instruction> _labels = new();
        private List<(BoundLabel label, Instruction instruction)> _fixups = new();
        private static readonly string[] stringArray = ["System.String"];

        private Emitter(string moduleName, string[] references)
        {
            var assemblyName = new AssemblyNameDefinition(moduleName, new Version(1, 0));
            _assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ModuleKind.Dll);

            _references = new ReferenceResolver(_assemblyDefinition.MainModule, _diagnostics);
            _interop = new InteropEmitter(_references);

            // Runtime assemblies go in first so that resolution, which takes the first match in
            // load order, finds real definitions rather than forwarding facades.
            _references.AddAssemblies(ReferenceAssemblyLocator.LoadRuntimeAssemblies());

            // ProLang.Runtime supplies the builtins. It goes in after the BCL so that a program
            // referencing an assembly of the same name cannot shadow it.
            var runtimeLibrary = RuntimeLibrary.TryLoad();

            if (runtimeLibrary != null)
            {
                _references.AddAssembly(runtimeLibrary);
            }

            foreach (var reference in references)
            {
                _references.AddReferenceFile(reference);
            }

            _stringConcatReference = ResolveMethod("System.String", "Concat", ["System.Object", "System.Object"])!;
            _dictionaryType = ResolveType("System.Collections.Generic.Dictionary`2")!;
        }

        /// <inheritdoc cref="ReferenceResolver.ResolveType"/>
        private TypeReference? ResolveType(string metaDataName) => _references.ResolveType(metaDataName);

        /// <inheritdoc cref="ReferenceResolver.ResolveMethod"/>
        private MethodReference? ResolveMethod(string typeName, string methodName, string[] parameterTypeNames) =>
            _references.ResolveMethod(typeName, methodName, parameterTypeNames);

        /// <inheritdoc cref="ReferenceResolver.GetRequiredType"/>
        private TypeReference GetCachedType(string metaDataName) => _references.GetRequiredType(metaDataName);

        /// <inheritdoc cref="ReferenceResolver.GetGenericMethod"/>
        private MethodReference GetGenericMethod(TypeReference type, string methodName, int parameterCount) =>
            _references.GetGenericMethod(type, methodName, parameterCount);

        private TypeReference GetTypeReference(TypeSymbol type)
        {
            if (_knownTypes.TryGetValue(type, out var typeReference))
                return typeReference;

            if (type is EnumSymbol)
            {
                var intRef = GetCachedType("System.Int32");
                _knownTypes.Add(type, intRef);
                return intRef;
            }

            if (type is StructSymbol structType)
            {
                if (!_structTypes.TryGetValue(structType.Name, out var structTypeDef))
                {
                    // Lazily emit instantiated generic struct types encountered during emission.
                    EmitStructType(structType);
                    _structTypes.TryGetValue(structType.Name, out structTypeDef);
                }
                var structTypeRef = _assemblyDefinition.MainModule.ImportReference(structTypeDef!);
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
                    "uint32" => GetCachedType("System.UInt32"),
                    "int16" => GetCachedType("System.Int16"),
                    "uint16" => GetCachedType("System.UInt16"),
                    "int8" => GetCachedType("System.SByte"),
                    "uint8" => GetCachedType("System.Byte"),
                    "int64" => GetCachedType("System.Int64"),
                    "uint64" => GetCachedType("System.UInt64"),
                    "float32" => GetCachedType("System.Single"),
                    "float64" => GetCachedType("System.Double"),
                    "float" => GetCachedType("System.Double"),
                    "string" => GetCachedType("System.String"),
                    "void" => GetCachedType("System.Void"),
                    "array" => new ArrayType(GetCachedType("System.Object")),
                    "map" => _dictionaryType.MakeGenericInstanceType(GetCachedType("System.Object"), GetCachedType("System.Object")),
                    _ => throw new Exception($"Unexpected type {type.Name}")
                };
            }
            else
            {
                if (type.Name == "array")
                {
                    var elementType = GetTypeReference(type.TypeArguments[0]);
                    resolved = new ArrayType(elementType);
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
            _structTypes[structSymbol.Name] = typeDef;
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

        /// <summary>
        /// Emits the assembly to a stream instead of a file. Used by tests and benchmarks so that
        /// disk I/O does not sit inside the measured region and temp files are not required.
        /// No <c>.runtimeconfig.json</c> is produced — there is no path to derive it from.
        /// </summary>
        public static ImmutableArray<Diagnostic> Emit(BoundProgram program, string moduleName, string[] references, Stream outputStream)
        {
            if (program.Diagnostics.Any())
            {
                return program.Diagnostics;
            }

            var emitter = new Emitter(moduleName, references);

            return emitter.Emit(program, outputStream, runtimeConfigPath: null);
        }

        public ImmutableArray<Diagnostic> Emit(BoundProgram program, string outputPath)
        {
            if (_diagnostics.Any())
            {
                return [.. _diagnostics];
            }

            BuildAssembly(program);

            if (_diagnostics.Any())
            {
                return _diagnostics.ToImmutableArray();
            }

            _assemblyDefinition.Write(outputPath);
            WriteRuntimeConfig(Path.ChangeExtension(outputPath, ".runtimeconfig.json"));

            return _diagnostics.ToImmutableArray();
        }

        private ImmutableArray<Diagnostic> Emit(BoundProgram program, Stream outputStream, string? runtimeConfigPath)
        {
            if (_diagnostics.Any())
            {
                return [.. _diagnostics];
            }

            BuildAssembly(program);

            if (_diagnostics.Any())
            {
                return _diagnostics.ToImmutableArray();
            }

            _assemblyDefinition.Write(outputStream);
            WriteRuntimeConfig(runtimeConfigPath);

            return _diagnostics.ToImmutableArray();
        }

        /// <summary>
        /// Builds the in-memory assembly for <paramref name="program"/>. Shared by every
        /// <c>Emit</c> overload; the caller decides where the result is written.
        /// </summary>
        private void BuildAssembly(BoundProgram program)
        {
            var objectType = GetTypeReference(TypeSymbol.Any);

            //main class or running class
            _typeDefinition = new TypeDefinition("", "Program", CecilTypeAttributes.Abstract | CecilTypeAttributes.Sealed, objectType);

            _assemblyDefinition.MainModule.Types.Add(_typeDefinition);

            // Emit output collection infrastructure for all assemblies
            // (libraries may have functions that use print())
            ResolveOutputHelpers();

            foreach (var structType in program.StructTypes)
            {
                // Skip generic templates — only concrete instantiations are emitted
                if (structType.IsGeneric) continue;
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
        }

        /// <summary>
        /// Writes the <c>.runtimeconfig.json</c> that lets <c>dotnet &lt;output&gt;.dll</c> resolve a
        /// shared framework. A null path means the caller does not want one (stream emit).
        /// </summary>
        private static void WriteRuntimeConfig(string? runtimeConfigPath)
        {
            if (runtimeConfigPath == null)
            {
                return;
            }

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

        /// <summary>
        /// Resolves the output-buffering entry points in <c>ProLang.Runtime</c>.
        /// </summary>
        /// <remarks>
        /// These were three methods and a <c>StringBuilder</c> field synthesised into every
        /// compiled assembly as hand-written IL. They are now ordinary C# in
        /// <see cref="RuntimeLibrary.Output"/>, and all this has to do is find them.
        /// <para>
        /// A failure to resolve reports a diagnostic through <see cref="ReferenceResolver"/>,
        /// which stops the assembly being written — a program whose <c>print()</c> calls emit
        /// nothing would otherwise silently produce no output.
        /// </para>
        /// </remarks>
        private void ResolveOutputHelpers()
        {
            if (_outputInfrastructureGenerated)
            {
                return;
            }

            _outputInitMethod = ResolveMethod(RuntimeLibrary.Output, "Initialize", []);
            _outputAppendMethod = ResolveMethod(RuntimeLibrary.Output, "Write", ["System.Object"]);
            _outputFlushMethod = ResolveMethod(RuntimeLibrary.Output, "Flush", []);

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
            ilProcessor.Emit(OpCodes.Call, _outputInitMethod);

            // 2. Prepare to call __UserMain
            // If __UserMain expects args, pass the string[] directly
            // If __UserMain takes no args, don't pass anything

            if (userMainFunction.Parameters.Any())
            {
                // __UserMain expects array<string> parameter - pass args directly
                // (string[] from CLR maps directly to array<string> in ProLang IL)
                ilProcessor.Emit(OpCodes.Ldarg_0);  // Load args parameter
                ilProcessor.Emit(OpCodes.Call, _methods[userMainFunction]);
            }
            else
            {
                // __UserMain takes no parameters - just call it
                ilProcessor.Emit(OpCodes.Call, _methods[userMainFunction]);
            }

            // 3. Call __FlushOutput() to print accumulated output
            ilProcessor.Emit(OpCodes.Call, _outputFlushMethod);

            // 4. Return void
            ilProcessor.Emit(OpCodes.Ret);

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
                ilProcessor.Emit(OpCodes.Ret);
            }
            else if (function.Type == TypeSymbol.Any && method.ReturnType.IsValueType)
            {
                ilProcessor.Emit(OpCodes.Box, method.ReturnType);
                ilProcessor.Emit(OpCodes.Ret);
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
                ilProcessor.Emit(OpCodes.Pop);
            }
        }

        private void EmitReturnStatement(ILProcessor ilProcessor, BoundReturnStatement node)
        {
            if (node.Expression != null)
            {
                EmitExpression(ilProcessor, node.Expression);
            }

            ilProcessor.Emit(OpCodes.Ret);
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
                ilProcessor.Emit(OpCodes.Ldloca, variableDefinition);
                ilProcessor.Emit(OpCodes.Initobj, typeReference);
            }

            EmitExpression(ilProcessor, node.Initializer);

            if (node.Variable.Type == TypeSymbol.Any && typeReference.IsValueType)
            {
                ilProcessor.Emit(OpCodes.Box, typeReference);
            }

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
                case BoundNodeKind.BoundArrayNewExpression:
                    EmitArrayNewExpression(ilProcessor, (BoundArrayNewExpression)node);
                    break;
                case BoundNodeKind.BoundEnumMemberExpression:
                    ilProcessor.Emit(OpCodes.Ldc_I4, ((BoundEnumMemberExpression)node).Member.Value);
                    break;
                default:
                    throw new NotSupportedException($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitIndexAssignmentExpression(ILProcessor ilProcessor, BoundIndexAssignmentExpression node)
        {
            if (node.LHS.Type.Name == "array")
            {
                var elementTypeRef = node.LHS.Type.TypeArguments.Length > 0
                    ? GetTypeReference(node.LHS.Type.TypeArguments[0])
                    : GetCachedType("System.Object");
                EmitExpression(ilProcessor, node.LHS);
                EmitExpression(ilProcessor, node.Index);
                EmitExpression(ilProcessor, node.RHS);
                EmitStelemForType(ilProcessor, elementTypeRef);
                // stelem is void; push null as dummy return value
                ilProcessor.Emit(OpCodes.Ldnull);
            }
            else
            {
                EmitExpression(ilProcessor, node.LHS);
                EmitExpression(ilProcessor, node.Index);
                EmitExpression(ilProcessor, node.RHS);
                var collectionType = GetTypeReference(node.LHS.Type);
                var setMethod = GetGenericMethod(collectionType, "set_Item", 2);
                ilProcessor.Emit(OpCodes.Callvirt, setMethod);
                ilProcessor.Emit(OpCodes.Ldnull);
            }
        }

        private void EmitIndexExpression(ILProcessor ilProcessor, BoundIndexExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);
            EmitExpression(ilProcessor, node.Index);

            if (node.Expression.Type.Name == "array")
            {
                var elementTypeRef = node.Expression.Type.TypeArguments.Length > 0
                    ? GetTypeReference(node.Expression.Type.TypeArguments[0])
                    : GetCachedType("System.Object");
                EmitLdelemForType(ilProcessor, elementTypeRef);
            }
            else
            {
                var collectionType = GetTypeReference(node.Expression.Type);
                var getMethod = GetGenericMethod(collectionType, "get_Item", 1);
                ilProcessor.Emit(OpCodes.Callvirt, getMethod);
            }
        }

        private void EmitMapExpression(ILProcessor ilProcessor, BoundMapExpression node)
        {
            var mapType = GetTypeReference(node.Type);
            var constructor = GetGenericMethod(mapType, ".ctor", 0);
            var addMethod = GetGenericMethod(mapType, "Add", 2);

            ilProcessor.Emit(OpCodes.Newobj, constructor);
            foreach (var entry in node.Entries)
            {
                ilProcessor.Emit(OpCodes.Dup);
                EmitExpression(ilProcessor, entry.Key);
                EmitExpression(ilProcessor, entry.Value);
                ilProcessor.Emit(OpCodes.Callvirt, addMethod);
            }
        }

        private void EmitArrayExpression(ILProcessor ilProcessor, BoundArrayExpression node)
        {
            var elementTypeRef = node.Type.TypeArguments.Length > 0
                ? GetTypeReference(node.Type.TypeArguments[0])
                : GetCachedType("System.Object");

            ilProcessor.Emit(OpCodes.Ldc_I4, node.Elements.Length);
            ilProcessor.Emit(OpCodes.Newarr, elementTypeRef);

            for (int i = 0; i < node.Elements.Length; i++)
            {
                ilProcessor.Emit(OpCodes.Dup);
                ilProcessor.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(ilProcessor, node.Elements[i]);
                EmitStelemForType(ilProcessor, elementTypeRef);
            }
        }

        private void EmitArrayNewExpression(ILProcessor ilProcessor, BoundArrayNewExpression node)
        {
            var elementTypeRef = GetTypeReference(node.ElementType);
            EmitExpression(ilProcessor, node.SizeExpression);
            ilProcessor.Emit(OpCodes.Newarr, elementTypeRef);
        }

        private void EmitStelemForType(ILProcessor ilProcessor, TypeReference elementType)
        {
            switch (elementType.FullName)
            {
                case "System.Boolean":
                case "System.Int32":
                case "System.UInt32":
                    ilProcessor.Emit(OpCodes.Stelem_I4); break;
                case "System.Byte":
                case "System.SByte":
                    ilProcessor.Emit(OpCodes.Stelem_I1); break;
                case "System.Int16":
                case "System.UInt16":
                    ilProcessor.Emit(OpCodes.Stelem_I2); break;
                case "System.Int64":
                case "System.UInt64":
                    ilProcessor.Emit(OpCodes.Stelem_I8); break;
                case "System.Single":
                    ilProcessor.Emit(OpCodes.Stelem_R4); break;
                case "System.Double":
                    ilProcessor.Emit(OpCodes.Stelem_R8); break;
                default:
                    ilProcessor.Emit(OpCodes.Stelem_Ref); break;
            }
        }

        private void EmitLdelemForType(ILProcessor ilProcessor, TypeReference elementType)
        {
            switch (elementType.FullName)
            {
                case "System.Boolean":
                case "System.Int32":
                    ilProcessor.Emit(OpCodes.Ldelem_I4); break;
                case "System.UInt32":
                    ilProcessor.Emit(OpCodes.Ldelem_U4); break;
                case "System.Byte":
                    ilProcessor.Emit(OpCodes.Ldelem_U1); break;
                case "System.SByte":
                    ilProcessor.Emit(OpCodes.Ldelem_I1); break;
                case "System.Int16":
                    ilProcessor.Emit(OpCodes.Ldelem_I2); break;
                case "System.UInt16":
                    ilProcessor.Emit(OpCodes.Ldelem_U2); break;
                case "System.Int64":
                    ilProcessor.Emit(OpCodes.Ldelem_I8); break;
                case "System.UInt64":
                    ilProcessor.Emit(OpCodes.Ldelem_I8); break; // CLR has no Ldelem_U8; use I8 (same bits)
                case "System.Single":
                    ilProcessor.Emit(OpCodes.Ldelem_R4); break;
                case "System.Double":
                    ilProcessor.Emit(OpCodes.Ldelem_R8); break;
                default:
                    ilProcessor.Emit(OpCodes.Ldelem_Ref); break;
            }
        }

        private void EmitConversionExpression(ILProcessor ilProcessor, BoundConversionExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);

            var fromType = node.Expression.Type;
            var toType = node.Type;

            if (toType == TypeSymbol.String)
            {
                // Box value types before calling ToString()
                if (IsValueType(fromType))
                    ilProcessor.Emit(OpCodes.Box, GetTypeReference(fromType));

                if (fromType != TypeSymbol.String)
                {
                    var toStringMethod = GetTypeReference(TypeSymbol.Any).Resolve().Methods.First(m => m.Name == "ToString" && m.Parameters.Count == 0);
                    ilProcessor.Emit(OpCodes.Callvirt, _assemblyDefinition.MainModule.ImportReference(toStringMethod));
                }
            }
            else if (fromType == TypeSymbol.Any || toType == TypeSymbol.Any)
            {
                if (toType == TypeSymbol.Any && IsValueType(fromType))
                    ilProcessor.Emit(OpCodes.Box, GetTypeReference(fromType));
                else if (fromType == TypeSymbol.Any && IsValueType(toType))
                    ilProcessor.Emit(OpCodes.Unbox_Any, GetTypeReference(toType));
            }
            else if (IsNumericType(fromType) && IsNumericType(toType))
            {
                ilProcessor.Emit(NumericConvOpCode(toType));
            }
        }

        private static OpCode NumericConvOpCode(TypeSymbol to)
        {
            if (to == TypeSymbol.Int8)   return OpCodes.Conv_I1;
            if (to == TypeSymbol.Int16)  return OpCodes.Conv_I2;
            if (to == TypeSymbol.Int)    return OpCodes.Conv_I4;
            if (to == TypeSymbol.Int64)  return OpCodes.Conv_I8;
            if (to == TypeSymbol.UInt8)  return OpCodes.Conv_U1;
            if (to == TypeSymbol.UInt16) return OpCodes.Conv_U2;
            if (to == TypeSymbol.UInt32)  return OpCodes.Conv_U4;
            if (to == TypeSymbol.UInt64)  return OpCodes.Conv_U8;
            if (to == TypeSymbol.Float32) return OpCodes.Conv_R4;
            if (to == TypeSymbol.Float64 || to == TypeSymbol.Float) return OpCodes.Conv_R8;
            return OpCodes.Nop;
        }

        private static bool IsNumericType(TypeSymbol type) =>
            type == TypeSymbol.Int    || type == TypeSymbol.Int8   || type == TypeSymbol.Int16  || type == TypeSymbol.Int64  ||
            type == TypeSymbol.UInt8  || type == TypeSymbol.UInt16 || type == TypeSymbol.UInt32 || type == TypeSymbol.UInt64 ||
            type == TypeSymbol.Float32 || type == TypeSymbol.Float64 || type == TypeSymbol.Float;

        private static bool IsValueType(TypeSymbol type) =>
            type == TypeSymbol.Int    || type == TypeSymbol.Bool   ||
            type == TypeSymbol.UInt32 || type == TypeSymbol.Int8   ||
            type == TypeSymbol.UInt8  || type == TypeSymbol.Int16  ||
            type == TypeSymbol.UInt16 || type == TypeSymbol.Int64  ||
            type == TypeSymbol.UInt64 || type == TypeSymbol.Float32 ||
            type == TypeSymbol.Float64 || type == TypeSymbol.Float  ||
            type is StructSymbol;

        private void EmitCastExpression(ILProcessor ilProcessor, BoundCastExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);

            var targetType = node.TargetType;

            // Safe casting from 'any' type to target type
            if (targetType == TypeSymbol.Int || targetType == TypeSymbol.Bool)
            {
                // Unbox from object to value type - throws InvalidCastException if type mismatch
                ilProcessor.Emit(OpCodes.Unbox_Any, GetTypeReference(targetType));
            }
            else if (targetType == TypeSymbol.String)
            {
                // Cast to string - value is already object
                ilProcessor.Emit(OpCodes.Isinst, GetTypeReference(targetType));
            }
            else if (targetType.Name == "array" || targetType.Name == "map")
            {
                // For collection types, just cast with isinst (reference types)
                ilProcessor.Emit(OpCodes.Isinst, GetTypeReference(targetType));
            }
            else
            {
                // For other types (structs, etc.), use isinst
                ilProcessor.Emit(OpCodes.Isinst, GetTypeReference(targetType));
            }
        }

        /// <summary>
        /// Emits a call: arguments first, then dispatch on what kind of function is being called.
        /// </summary>
        /// <remarks>
        /// Three kinds of callee, checked in order of specificity:
        /// a builtin with a dedicated IL sequence (see <see cref="IntrinsicRegistry"/>), an
        /// interop symbol backed by reflection, or an ordinary user function emitted into this
        /// same assembly.
        /// </remarks>
        private void EmitCallExpression(ILProcessor ilProcessor, BoundCallExpression node)
        {
            foreach (var argument in node.Arguments)
            {
                EmitExpression(ilProcessor, argument);
            }

            // print() is the one builtin that cannot live in the registry: it routes through
            // output-collection infrastructure that is synthesised into the assembly being
            // emitted, so its target does not exist until EmitOutputHelpers has run.
            if (node.Function == BuiltInFunctions.Print)
            {
                if (_outputAppendMethod == null)
                {
                    throw new InvalidOperationException(
                        "Output infrastructure must be emitted before any call to print().");
                }

                ilProcessor.Emit(OpCodes.Call, _outputAppendMethod);
                return;
            }

            if (IntrinsicRegistry.TryGetEmitter(node.Function, out var intrinsic))
            {
                intrinsic(new IntrinsicContext(ilProcessor, _references, _diagnostics, GetTypeReference));
                return;
            }

            switch (node.Function)
            {
                // An enum member is a compile-time constant; there is nothing to call.
                case DotNetEnumConstantSymbol enumConstant:
                    ilProcessor.Emit(OpCodes.Ldc_I4, enumConstant.Value);
                    return;

                case DotNetFunctionSymbol dotNetFunction:
                    _interop.EmitCall(ilProcessor, dotNetFunction);
                    return;

                default:
                    ilProcessor.Emit(OpCodes.Call, _methods[node.Function]);
                    return;
            }
        }


        /// <summary>
        /// Boxes the value just emitted if <c>String.Concat(object, object)</c> would otherwise
        /// receive an unboxed value type.
        /// </summary>
        /// <remarks>
        /// The test is on the emitted Cecil type rather than an enumerated list of ProLang types.
        /// The previous version listed only <c>any</c>, <c>int</c>, and <c>bool</c>, so
        /// <c>"x" + someInt64</c> — and equally <c>float64</c>, <c>uint8</c>, or any struct —
        /// passed a raw value where a reference was required, producing IL that fails
        /// verification. Strings are already references and must not be boxed.
        /// </remarks>
        private void BoxForStringConcat(ILProcessor ilProcessor, TypeSymbol operandType)
        {
            if (operandType == TypeSymbol.String)
            {
                return;
            }

            var typeReference = GetTypeReference(operandType);

            if (typeReference.IsValueType)
            {
                ilProcessor.Emit(OpCodes.Box, typeReference);
            }
        }

        private void EmitBinaryExpression(ILProcessor ilProcessor, BoundBinaryExpression node)
        {
            var isStringConcatenation =
                node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String;

            EmitExpression(ilProcessor, node.Left);
            if (isStringConcatenation)
            {
                BoxForStringConcat(ilProcessor, node.Left.Type);
            }

            EmitExpression(ilProcessor, node.Right);
            if (isStringConcatenation)
            {
                BoxForStringConcat(ilProcessor, node.Right.Type);
            }

            if (node.Op.Kind == BoundBinaryOperatorKind.Addition)
            {
                if (node.Op.Type == TypeSymbol.String)
                {
                    ilProcessor.Emit(OpCodes.Call, _stringConcatReference);
                }
                else
                {
                    ilProcessor.Emit(OpCodes.Add);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Subtraction)
            {
                ilProcessor.Emit(OpCodes.Sub);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Multiplication)
            {
                ilProcessor.Emit(OpCodes.Mul);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Division)
            {
                ilProcessor.Emit(OpCodes.Div);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LogicalAnd)
            {
                ilProcessor.Emit(OpCodes.And);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LogicalOr)
            {
                ilProcessor.Emit(OpCodes.Or);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Equals)
            {
                if (node.Left.Type == TypeSymbol.String || node.Right.Type == TypeSymbol.String)
                {
                    var eqMethod = ResolveMethod("System.String", "op_Equality", new[] { "System.String", "System.String" });
                    if (eqMethod != null)
                        ilProcessor.Emit(OpCodes.Call, eqMethod);
                    else
                        ilProcessor.Emit(OpCodes.Ceq);
                }
                else
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.NotEquals)
            {
                if (node.Left.Type == TypeSymbol.String || node.Right.Type == TypeSymbol.String)
                {
                    var neqMethod = ResolveMethod("System.String", "op_Inequality", new[] { "System.String", "System.String" });
                    if (neqMethod != null)
                        ilProcessor.Emit(OpCodes.Call, neqMethod);
                    else
                    {
                        ilProcessor.Emit(OpCodes.Ceq);
                        ilProcessor.Emit(OpCodes.Ldc_I4_0);
                        ilProcessor.Emit(OpCodes.Ceq);
                    }
                }
                else
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LessThan)
            {
                ilProcessor.Emit(OpCodes.Clt);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LessEqual)
            {
                ilProcessor.Emit(OpCodes.Cgt);
                ilProcessor.Emit(OpCodes.Ldc_I4_0);
                ilProcessor.Emit(OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.GreaterThan)
            {
                ilProcessor.Emit(OpCodes.Cgt);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.GreaterEqual)
            {
                ilProcessor.Emit(OpCodes.Clt);
                ilProcessor.Emit(OpCodes.Ldc_I4_0);
                ilProcessor.Emit(OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Modulo)
            {
                ilProcessor.Emit(OpCodes.Rem);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseAnd)
            {
                ilProcessor.Emit(OpCodes.And);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseOr)
            {
                ilProcessor.Emit(OpCodes.Or);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseXor)
            {
                ilProcessor.Emit(OpCodes.Xor);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseLeftShift)
            {
                ilProcessor.Emit(OpCodes.Shl);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseRightShift)
            {
                ilProcessor.Emit(OpCodes.Shr_Un);
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
                ilProcessor.Emit(OpCodes.Neg);
            }
            else if (node.Op.Kind == BoundUnaryOperatorKind.LogicalNegation)
            {
                ilProcessor.Emit(OpCodes.Ldc_I4_0);
                ilProcessor.Emit(OpCodes.Ceq);
            }
            else
            {
                throw new Exception($"Unexpected unary operator {node.Op.Kind}");
            }
        }

        private void EmitAssignmentExpression(ILProcessor ilProcessor, BoundAssignmentExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);
            ilProcessor.Emit(OpCodes.Dup);

            if (node.Variable is ParameterSymbol parameter)
            {
                ilProcessor.Emit(OpCodes.Starg, ilProcessor.Body.Method.Parameters[parameter.Ordinal]);
            }
            else
            {
                var variableDefinition = _locals[node.Variable];
                ilProcessor.Emit(OpCodes.Stloc, variableDefinition);
            }
        }

        private void EmitVariableExpression(ILProcessor ilProcessor, BoundVariableExpression node)
        {
            if (node.Variable is ParameterSymbol parameter)
            {
                ilProcessor.Emit(OpCodes.Ldarg, ilProcessor.Body.Method.Parameters[parameter.Ordinal]);
            }
            else
            {
                var variableDefinition = _locals[node.Variable];

                ilProcessor.Emit(OpCodes.Ldloc, variableDefinition);
            }
        }

        private void EmitLiteralExpression(ILProcessor ilProcessor, BoundLiteralExpression node)
        {
            if (node.Type == TypeSymbol.Bool)
            {
                var value = (bool)node.Value;

                var instruction = value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;

                ilProcessor.Emit(instruction);
            }
            else if (node.Type == TypeSymbol.Int
            || node.Type == TypeSymbol.UInt32
            || node.Type == TypeSymbol.Int16
            || node.Type == TypeSymbol.UInt16
            || node.Type == TypeSymbol.UInt8)
            {
                var value = Convert.ToInt32(node.Value);

                ilProcessor.Emit(OpCodes.Ldc_I4, value);
            }
            else if (node.Type == TypeSymbol.Int8)//Chosen the more efficient instruction
            {
                var value = (sbyte)node.Value;

                ilProcessor.Emit(OpCodes.Ldc_I4_S, value);
            }
            else if (node.Type == TypeSymbol.Int64
            || node.Type == TypeSymbol.UInt64)
            {
                var value = (long)node.Value;

                ilProcessor.Emit(OpCodes.Ldc_I8, value);
            }
            else if (node.Type == TypeSymbol.Float32)
            {
                ilProcessor.Emit(OpCodes.Ldc_R4, (float)node.Value);
            }
            else if (node.Type == TypeSymbol.Float64 || node.Type == TypeSymbol.Float)
            {
                ilProcessor.Emit(OpCodes.Ldc_R8, (double)node.Value);
            }
            else if (node.Type == TypeSymbol.String)
            {
                var value = (string)node.Value;

                ilProcessor.Emit(OpCodes.Ldstr, value);
            }
            else
            {
                throw new NotImplementedException($"Unexpected literal type: {node.Type}");
            }
        }

        private void EmitStructCreationExpression(ILProcessor ilProcessor, BoundStructCreationExpression node)
        {
            var structSymbol = node.StructType;
            var typeDef = _structTypes[structSymbol.Name];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);

            var localVar = new VariableDefinition(typeRef);
            ilProcessor.Body.Variables.Add(localVar);

            foreach (var field in structSymbol.Fields)
            {
                ilProcessor.Emit(OpCodes.Ldloca, localVar);

                var fieldIndex = structSymbol.Fields.IndexOf(field);
                var fieldValue = node.FieldValues[fieldIndex];
                EmitExpression(ilProcessor, fieldValue);

                var fieldType = GetTypeReference(field.Type);

                // Box only when storing a value type into an `any` field, which is
                // System.Object at the IL level. The condition here used to be inverted —
                // `field.Type != any && !fieldType.IsValueType` — which boxed *reference* types
                // into non-`any` fields, emitting `box string` and `box Object[]`.
                if (field.Type == TypeSymbol.Any && GetTypeReference(fieldValue.Type).IsValueType)
                {
                    ilProcessor.Emit(OpCodes.Box, GetTypeReference(fieldValue.Type));
                }

                var fieldRef = new FieldReference(field.Name, fieldType);
                fieldRef.DeclaringType = typeRef;
                ilProcessor.Emit(OpCodes.Stfld, fieldRef);
            }

            ilProcessor.Emit(OpCodes.Ldloc, localVar);
        }

        private void EmitFieldAccessExpression(ILProcessor ilProcessor, BoundFieldAccessExpression node)
        {
            EmitExpression(ilProcessor, node.Expression);

            var structSymbol = (StructSymbol)node.Expression.Type;
            var typeDef = _structTypes[structSymbol.Name];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);
            var fieldType = GetTypeReference(node.Field.Type);
            var fieldRef = new FieldReference(node.FieldName, fieldType);
            fieldRef.DeclaringType = typeRef;

            ilProcessor.Emit(OpCodes.Ldfld, fieldRef);

            if (node.Type == TypeSymbol.Any && fieldType.IsValueType)
            {
                ilProcessor.Emit(OpCodes.Box, fieldType);
            }
        }

        private void EmitFieldAssignmentExpression(ILProcessor ilProcessor, BoundFieldAssignmentExpression node)
        {
            var structSymbol = (StructSymbol)node.Expression.Type;
            var typeDef = _structTypes[structSymbol.Name];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);
            var fieldType = GetTypeReference(node.Field.Type);
            var fieldRef = new FieldReference(node.FieldName, fieldType);
            fieldRef.DeclaringType = typeRef;

            if (node.Expression is BoundVariableExpression varExpr)
            {
                VariableDefinition? varDef = null;
                if (varExpr.Variable is ParameterSymbol paramSym)
                {
                    ilProcessor.Emit(OpCodes.Ldarga, ilProcessor.Body.Method.Parameters[paramSym.Ordinal]);
                }
                else
                {
                    varDef = _locals[varExpr.Variable];
                    ilProcessor.Emit(OpCodes.Ldloca, varDef);
                }

                EmitExpression(ilProcessor, node.Value);
                ilProcessor.Emit(OpCodes.Dup);

                var tempVar = new VariableDefinition(fieldType);
                ilProcessor.Body.Variables.Add(tempVar);
                ilProcessor.Emit(OpCodes.Stloc, tempVar);

                ilProcessor.Emit(OpCodes.Stfld, fieldRef);

                ilProcessor.Emit(OpCodes.Ldloc, tempVar);
            }
            else
            {
                EmitExpression(ilProcessor, node.Expression);
                ilProcessor.Emit(OpCodes.Dup);
                EmitExpression(ilProcessor, node.Value);
                ilProcessor.Emit(OpCodes.Stfld, fieldRef);
            }
        }
    }
}
