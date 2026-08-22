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
        private MethodReference? _stringConcatStringsReference;

        // Entry points into ProLang.Runtime.Output, which buffers print() and flushes at exit.
        private MethodReference? _outputInitMethod;
        private MethodReference? _outputAppendMethod;
        private MethodReference? _outputFlushMethod;
        private bool _outputInfrastructureGenerated = false;

        private readonly TypeReference _dictionaryType;

        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();


        // Keyed by struct name so concrete instantiations (DynArray<int>) are deduplicated by name.
        private Dictionary<string, TypeDefinition> _structTypes = new(StringComparer.Ordinal);

        private TypeDefinition _typeDefinition;

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
            if (program.MainFunction != null && program.MainFunction.Name == SyntheticNames.Main)
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
            var userMainFunction = program.Functions.Keys.FirstOrDefault(f => f.Name == SyntheticNames.UserMain);
            if (userMainFunction == null)
                return;

            var voidType = GetTypeReference(TypeSymbol.Void);
            var stringType = ResolveType("System.String");
            var stringArrayType = stringType?.MakeArrayType();

            // Create __Main(string[] args) method
            var mainMethod = new MethodDefinition(SyntheticNames.Main,
                CecilMethodAttributes.Static | CecilMethodAttributes.Public,
                voidType);

            // Add string[] args parameter
            mainMethod.Parameters.Add(new ParameterDefinition("args",
                CecilParameterAttributes.None,
                stringArrayType));

            var scope = new MethodBodyScope(mainMethod);

            // 1. Start the runtime's output buffer
            scope.IL.Emit(OpCodes.Call, _outputInitMethod);

            // 2. Prepare to call __UserMain
            // If __UserMain expects args, pass the string[] directly
            // If __UserMain takes no args, don't pass anything

            if (userMainFunction.Parameters.Any())
            {
                // __UserMain expects array<string> parameter - pass args directly
                // (string[] from CLR maps directly to array<string> in ProLang IL)
                scope.IL.Emit(OpCodes.Ldarg_0);  // Load args parameter
                scope.IL.Emit(OpCodes.Call, _methods[userMainFunction]);
            }
            else
            {
                // __UserMain takes no parameters - just call it
                scope.IL.Emit(OpCodes.Call, _methods[userMainFunction]);
            }

            // 3. Call __FlushOutput() to print accumulated output
            scope.IL.Emit(OpCodes.Call, _outputFlushMethod);

            // 4. Return void
            scope.IL.Emit(OpCodes.Ret);

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

            // All per-method state lives in the scope, so nothing has to be reset between bodies
            // and emitting one body cannot disturb another.
            var scope = new MethodBodyScope(method);

            foreach (var statement in body.Statements)
            {
                EmitStatement(scope, statement);
            }

            if (method.ReturnType.FullName == "System.Void")
            {
                scope.IL.Emit(OpCodes.Ret);
            }
            else if (function.Type == TypeSymbol.Any && method.ReturnType.IsValueType)
            {
                scope.IL.Emit(OpCodes.Box, method.ReturnType);
                scope.IL.Emit(OpCodes.Ret);
            }

            scope.PatchBranches();

            method.Body.OptimizeMacros();
        }

        private void EmitStatement(MethodBodyScope scope, BoundStatement node)
        {
            switch (node.Kind)
            {
                case BoundNodeKind.VariableDeclaration:
                    EmitVariableDeclaration(scope, (BoundVariableDeclaration)node);
                    break;
                case BoundNodeKind.LabelStatement:
                    EmitLabelStatement(scope, (BoundLabelStatement)node);
                    break;
                case BoundNodeKind.GotoStatement:
                    EmitGotoStatement(scope, (BoundGotoStatement)node);
                    break;
                case BoundNodeKind.ConditionalGotoStatement:
                    EmitConditionalGotoStatement(scope, (BoundConditionalGotoStatement)node);
                    break;
                case BoundNodeKind.ReturnStatement:
                    EmitReturnStatement(scope, (BoundReturnStatement)node);
                    break;
                case BoundNodeKind.ExpressionStatement:
                    EmitExpressionStatement(scope, (BoundExpressionStatement)node);
                    break;
                default:
                    throw new Exception($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitExpressionStatement(MethodBodyScope scope, BoundExpressionStatement node)
        {
            EmitExpression(scope, node.Expression);

            if (node.Expression.Type != TypeSymbol.Void)
            {
                scope.IL.Emit(OpCodes.Pop);
            }
        }

        private void EmitReturnStatement(MethodBodyScope scope, BoundReturnStatement node)
        {
            if (node.Expression != null)
            {
                EmitExpression(scope, node.Expression);
            }

            scope.IL.Emit(OpCodes.Ret);
        }

        private void EmitConditionalGotoStatement(MethodBodyScope scope, BoundConditionalGotoStatement node)
        {
            EmitExpression(scope, node.Condition);

            // Create a placeholder instruction - we'll fixup the target later
            var opCode = node.JumpIfTrue ? OpCodes.Brtrue : OpCodes.Brfalse;
            var instruction = scope.IL.Create(opCode, Instruction.Create(OpCodes.Nop));
            scope.IL.Append(instruction);
            scope.RecordFixup(node.BoundLabel, instruction);
        }

        private void EmitGotoStatement(MethodBodyScope scope, BoundGotoStatement node)
        {
            // Create a placeholder instruction - we'll fixup the target later
            var instruction = scope.IL.Create(OpCodes.Br, Instruction.Create(OpCodes.Nop));
            scope.IL.Append(instruction);
            scope.RecordFixup(node.BoundLabel, instruction);
        }

        private void EmitLabelStatement(MethodBodyScope scope, BoundLabelStatement node)
        {
            var instruction = scope.IL.Create(OpCodes.Nop);
            scope.IL.Append(instruction);
            scope.MarkLabel(node.BoundLabel, instruction);
        }

        private void EmitVariableDeclaration(MethodBodyScope scope, BoundVariableDeclaration node)
        {
            var typeReference = GetTypeReference(node.Variable.Type);

            var variableDefinition = scope.DeclareLocal(node.Variable, typeReference);

            if (node.Variable.Type is StructSymbol)
            {
                scope.IL.Emit(OpCodes.Ldloca, variableDefinition);
                scope.IL.Emit(OpCodes.Initobj, typeReference);
            }

            EmitExpression(scope, node.Initializer);

            if (node.Variable.Type == TypeSymbol.Any && typeReference.IsValueType)
            {
                scope.IL.Emit(OpCodes.Box, typeReference);
            }

            scope.IL.Emit(OpCodes.Stloc, variableDefinition);
        }


        private void EmitExpression(MethodBodyScope scope, BoundExpression node)
        {
            switch (node.Kind)
            {
                case BoundNodeKind.BoundLiteralExpression:
                    EmitLiteralExpression(scope, (BoundLiteralExpression)node);
                    break;
                case BoundNodeKind.BoundVariableExpression:
                    EmitVariableExpression(scope, (BoundVariableExpression)node);
                    break;
                case BoundNodeKind.BoundAssignmentExpression:
                    EmitAssignmentExpression(scope, (BoundAssignmentExpression)node);
                    break;
                case BoundNodeKind.BoundUnaryExpression:
                    EmitUnaryExpression(scope, (BoundUnaryExpression)node);
                    break;
                case BoundNodeKind.BoundBinaryExpression:
                    EmitBinaryExpression(scope, (BoundBinaryExpression)node);
                    break;
                case BoundNodeKind.BoundCallExpression:
                    EmitCallExpression(scope, (BoundCallExpression)node);
                    break;
                case BoundNodeKind.BoundConversionExpression:
                    EmitConversionExpression(scope, (BoundConversionExpression)node);
                    break;
                case BoundNodeKind.BoundArrayExpression:
                    EmitArrayExpression(scope, (BoundArrayExpression)node);
                    break;
                case BoundNodeKind.BoundMapExpression:
                    EmitMapExpression(scope, (BoundMapExpression)node);
                    break;
                case BoundNodeKind.BoundIndexExpression:
                    EmitIndexExpression(scope, (BoundIndexExpression)node);
                    break;
                case BoundNodeKind.BoundIndexAssignmentExpression:
                    EmitIndexAssignmentExpression(scope, (BoundIndexAssignmentExpression)node);
                    break;
                case BoundNodeKind.BoundStructCreationExpression:
                    EmitStructCreationExpression(scope, (BoundStructCreationExpression)node);
                    break;
                case BoundNodeKind.BoundFieldAccessExpression:
                    EmitFieldAccessExpression(scope, (BoundFieldAccessExpression)node);
                    break;
                case BoundNodeKind.BoundFieldAssignmentExpression:
                    EmitFieldAssignmentExpression(scope, (BoundFieldAssignmentExpression)node);
                    break;
                case BoundNodeKind.BoundCastExpression:
                    EmitCastExpression(scope, (BoundCastExpression)node);
                    break;
                case BoundNodeKind.BoundArrayNewExpression:
                    EmitArrayNewExpression(scope, (BoundArrayNewExpression)node);
                    break;
                case BoundNodeKind.BoundEnumMemberExpression:
                    scope.IL.Emit(OpCodes.Ldc_I4, ((BoundEnumMemberExpression)node).Member.Value);
                    break;
                default:
                    throw new NotSupportedException($"Unexpected node kind {node.Kind}");
            }
        }

        private void EmitIndexAssignmentExpression(MethodBodyScope scope, BoundIndexAssignmentExpression node)
        {
            if (node.LHS.Type.Name == "array")
            {
                var elementTypeRef = node.LHS.Type.TypeArguments.Length > 0
                    ? GetTypeReference(node.LHS.Type.TypeArguments[0])
                    : GetCachedType("System.Object");
                EmitExpression(scope, node.LHS);
                EmitExpression(scope, node.Index);
                EmitExpression(scope, node.RHS);
                EmitStelemForType(scope, elementTypeRef);
                // stelem is void; push null as dummy return value
                scope.IL.Emit(OpCodes.Ldnull);
            }
            else
            {
                EmitExpression(scope, node.LHS);
                EmitExpression(scope, node.Index);
                EmitExpression(scope, node.RHS);
                var collectionType = GetTypeReference(node.LHS.Type);
                var setMethod = GetGenericMethod(collectionType, "set_Item", 2);
                scope.IL.Emit(OpCodes.Callvirt, setMethod);
                scope.IL.Emit(OpCodes.Ldnull);
            }
        }

        private void EmitIndexExpression(MethodBodyScope scope, BoundIndexExpression node)
        {
            EmitExpression(scope, node.Expression);
            EmitExpression(scope, node.Index);

            if (node.Expression.Type.Name == "array")
            {
                var elementTypeRef = node.Expression.Type.TypeArguments.Length > 0
                    ? GetTypeReference(node.Expression.Type.TypeArguments[0])
                    : GetCachedType("System.Object");
                EmitLdelemForType(scope, elementTypeRef);
            }
            else
            {
                var collectionType = GetTypeReference(node.Expression.Type);
                var getMethod = GetGenericMethod(collectionType, "get_Item", 1);
                scope.IL.Emit(OpCodes.Callvirt, getMethod);
            }
        }

        private void EmitMapExpression(MethodBodyScope scope, BoundMapExpression node)
        {
            var mapType = GetTypeReference(node.Type);
            var constructor = GetGenericMethod(mapType, ".ctor", 0);
            var addMethod = GetGenericMethod(mapType, "Add", 2);

            scope.IL.Emit(OpCodes.Newobj, constructor);
            foreach (var entry in node.Entries)
            {
                scope.IL.Emit(OpCodes.Dup);
                EmitExpression(scope, entry.Key);
                EmitExpression(scope, entry.Value);
                scope.IL.Emit(OpCodes.Callvirt, addMethod);
            }
        }

        private void EmitArrayExpression(MethodBodyScope scope, BoundArrayExpression node)
        {
            var elementTypeRef = node.Type.TypeArguments.Length > 0
                ? GetTypeReference(node.Type.TypeArguments[0])
                : GetCachedType("System.Object");

            scope.IL.Emit(OpCodes.Ldc_I4, node.Elements.Length);
            scope.IL.Emit(OpCodes.Newarr, elementTypeRef);

            for (int i = 0; i < node.Elements.Length; i++)
            {
                scope.IL.Emit(OpCodes.Dup);
                scope.IL.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(scope, node.Elements[i]);
                EmitStelemForType(scope, elementTypeRef);
            }
        }

        private void EmitArrayNewExpression(MethodBodyScope scope, BoundArrayNewExpression node)
        {
            var elementTypeRef = GetTypeReference(node.ElementType);
            EmitExpression(scope, node.SizeExpression);
            scope.IL.Emit(OpCodes.Newarr, elementTypeRef);
        }

        private void EmitStelemForType(MethodBodyScope scope, TypeReference elementType)
        {
            switch (elementType.FullName)
            {
                case "System.Boolean":
                case "System.Int32":
                case "System.UInt32":
                    scope.IL.Emit(OpCodes.Stelem_I4); break;
                case "System.Byte":
                case "System.SByte":
                    scope.IL.Emit(OpCodes.Stelem_I1); break;
                case "System.Int16":
                case "System.UInt16":
                    scope.IL.Emit(OpCodes.Stelem_I2); break;
                case "System.Int64":
                case "System.UInt64":
                    scope.IL.Emit(OpCodes.Stelem_I8); break;
                case "System.Single":
                    scope.IL.Emit(OpCodes.Stelem_R4); break;
                case "System.Double":
                    scope.IL.Emit(OpCodes.Stelem_R8); break;
                default:
                    scope.IL.Emit(OpCodes.Stelem_Ref); break;
            }
        }

        private void EmitLdelemForType(MethodBodyScope scope, TypeReference elementType)
        {
            switch (elementType.FullName)
            {
                case "System.Boolean":
                case "System.Int32":
                    scope.IL.Emit(OpCodes.Ldelem_I4); break;
                case "System.UInt32":
                    scope.IL.Emit(OpCodes.Ldelem_U4); break;
                case "System.Byte":
                    scope.IL.Emit(OpCodes.Ldelem_U1); break;
                case "System.SByte":
                    scope.IL.Emit(OpCodes.Ldelem_I1); break;
                case "System.Int16":
                    scope.IL.Emit(OpCodes.Ldelem_I2); break;
                case "System.UInt16":
                    scope.IL.Emit(OpCodes.Ldelem_U2); break;
                case "System.Int64":
                    scope.IL.Emit(OpCodes.Ldelem_I8); break;
                case "System.UInt64":
                    scope.IL.Emit(OpCodes.Ldelem_I8); break; // CLR has no Ldelem_U8; use I8 (same bits)
                case "System.Single":
                    scope.IL.Emit(OpCodes.Ldelem_R4); break;
                case "System.Double":
                    scope.IL.Emit(OpCodes.Ldelem_R8); break;
                default:
                    scope.IL.Emit(OpCodes.Ldelem_Ref); break;
            }
        }

        private void EmitConversionExpression(MethodBodyScope scope, BoundConversionExpression node)
        {
            EmitExpression(scope, node.Expression);

            var fromType = node.Expression.Type;
            var toType = node.Type;

            if (toType == TypeSymbol.String)
            {
                EmitStringConversion(scope, fromType);
            }
            else if (fromType == TypeSymbol.Any || toType == TypeSymbol.Any)
            {
                if (toType == TypeSymbol.Any && IsValueType(fromType))
                    scope.IL.Emit(OpCodes.Box, GetTypeReference(fromType));
                else if (fromType == TypeSymbol.Any && IsValueType(toType))
                    scope.IL.Emit(OpCodes.Unbox_Any, GetTypeReference(toType));
            }
            else if (IsNumericType(fromType) && IsNumericType(toType))
            {
                scope.IL.Emit(NumericConvOpCode(toType));
            }
        }

        /// <summary>
        /// Converts the value on top of the stack to a string, without boxing where possible.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>string(x)</c> used to compile to <c>box</c> plus a virtual
        /// <c>Object::ToString()</c>, allocating once per conversion. That was 72% of all boxing
        /// in the compiled corpus and a per-iteration allocation in any loop that formats a
        /// number, even though the operand's type is statically known right here.
        /// </para>
        /// <para>
        /// Each overload calls the same <c>ToString()</c> the virtual dispatch would have
        /// reached, so the resulting text is unchanged.
        /// </para>
        /// </remarks>
        private void EmitStringConversion(MethodBodyScope scope, TypeSymbol fromType)
        {
            if (fromType == TypeSymbol.String)
            {
                return;
            }

            var parameterType = RuntimeOverloadParameter(fromType);

            // A struct or anything else without a dedicated overload still goes through object.
            if (parameterType == null)
            {
                if (IsValueType(fromType))
                {
                    scope.IL.Emit(OpCodes.Box, GetTypeReference(fromType));
                }

                parameterType = "System.Object";
            }

            var from = ResolveMethod(RuntimeLibrary.StringOps, "From", [parameterType]);

            if (from == null)
            {
                // ResolveMethod reported the failure; emitting a partial sequence here would
                // leave the operand stranded on the stack.
                return;
            }

            scope.IL.Emit(OpCodes.Call, from);
        }

        /// <summary>
        /// The metadata name of the runtime overload parameter that takes <paramref name="type"/>
        /// without boxing, or <see langword="null"/> if there is no such overload.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The IL evaluation stack has no types narrower than <c>int32</c>, so <c>int8</c>,
        /// <c>uint8</c>, <c>int16</c>, and <c>uint16</c> are already sitting there as <c>int32</c>
        /// and can call the <c>int</c> overload directly. Their values survive: the signed types
        /// are sign-extended and the unsigned ones zero-extended when loaded.
        /// </para>
        /// <para>
        /// <c>uint32</c> cannot share that overload — the bit pattern is the same but
        /// <c>int32</c> would render anything above 2^31 as negative. <c>float32</c> likewise
        /// keeps its own overload, because <c>Single.ToString()</c> and
        /// <c>Double.ToString()</c> disagree on the same value.
        /// </para>
        /// </remarks>
        private static string? RuntimeOverloadParameter(TypeSymbol type)
        {
            if (type is EnumSymbol)
            {
                // Enums are erased to int32.
                return "System.Int32";
            }

            if (type == TypeSymbol.Int || type == TypeSymbol.Int8 || type == TypeSymbol.Int16
                || type == TypeSymbol.UInt8 || type == TypeSymbol.UInt16)
            {
                return "System.Int32";
            }

            if (type == TypeSymbol.UInt32) return "System.UInt32";
            if (type == TypeSymbol.Int64) return "System.Int64";
            if (type == TypeSymbol.UInt64) return "System.UInt64";
            if (type == TypeSymbol.Bool) return "System.Boolean";
            if (type == TypeSymbol.Float32) return "System.Single";
            if (type == TypeSymbol.Float64 || type == TypeSymbol.Float) return "System.Double";
            if (type == TypeSymbol.String) return "System.String";

            return null;
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

        private void EmitCastExpression(MethodBodyScope scope, BoundCastExpression node)
        {
            EmitExpression(scope, node.Expression);

            var targetType = node.TargetType;

            // Safe casting from 'any' type to target type
            if (targetType == TypeSymbol.Int || targetType == TypeSymbol.Bool)
            {
                // Unbox from object to value type - throws InvalidCastException if type mismatch
                scope.IL.Emit(OpCodes.Unbox_Any, GetTypeReference(targetType));
            }
            else if (targetType == TypeSymbol.String)
            {
                // Cast to string - value is already object
                scope.IL.Emit(OpCodes.Isinst, GetTypeReference(targetType));
            }
            else if (targetType.Name == "array" || targetType.Name == "map")
            {
                // For collection types, just cast with isinst (reference types)
                scope.IL.Emit(OpCodes.Isinst, GetTypeReference(targetType));
            }
            else
            {
                // For other types (structs, etc.), use isinst
                scope.IL.Emit(OpCodes.Isinst, GetTypeReference(targetType));
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
        private void EmitCallExpression(MethodBodyScope scope, BoundCallExpression node)
        {
            // print() is handled before its argument is emitted, because choosing a non-boxing
            // overload means emitting the argument's *underlying* value rather than the
            // converted-to-any one the binder produced.
            if (node.Function == BuiltInFunctions.Print)
            {
                EmitPrint(scope, node.Arguments[0]);
                return;
            }

            foreach (var argument in node.Arguments)
            {
                EmitExpression(scope, argument);
            }

            if (IntrinsicRegistry.TryGetEmitter(node.Function, out var intrinsic))
            {
                intrinsic(new IntrinsicContext(scope, _references, _diagnostics, GetTypeReference));
                return;
            }

            switch (node.Function)
            {
                // An enum member is a compile-time constant; there is nothing to call.
                case DotNetEnumConstantSymbol enumConstant:
                    scope.IL.Emit(OpCodes.Ldc_I4, enumConstant.Value);
                    return;

                case DotNetFunctionSymbol dotNetFunction:
                    _interop.EmitCall(scope.IL, dotNetFunction);
                    return;

                default:
                    scope.IL.Emit(OpCodes.Call, _methods[node.Function]);
                    return;
            }
        }


        /// <summary>
        /// Emits a <c>print</c> call, choosing an overload that does not box.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>print</c> declares its parameter as <c>any</c>, so the binder wraps every argument
        /// in a conversion to <see cref="object"/>. Emitting that conversion literally boxes each
        /// value. Looking through it recovers the static type, which was never actually lost, and
        /// lets a typed <c>Output.Write</c> overload take the value directly.
        /// </para>
        /// <para>
        /// This mirrors what the C backend has always done — see <c>CEmitter.UnwrapAny</c>, whose
        /// comment describes the same insight.
        /// </para>
        /// </remarks>
        private void EmitPrint(MethodBodyScope scope, BoundExpression argument)
        {
            if (_outputAppendMethod == null)
            {
                throw new InvalidOperationException(
                    "Output infrastructure must be resolved before any call to print().");
            }

            var value = UnwrapConversionToAny(argument);
            var parameterType = RuntimeOverloadParameter(value.Type);

            if (parameterType == null)
            {
                // No typed overload — emit the original argument, boxing included, and take the
                // object overload resolved during setup.
                EmitExpression(scope, argument);
                scope.IL.Emit(OpCodes.Call, _outputAppendMethod);

                return;
            }

            var write = ResolveMethod(RuntimeLibrary.Output, "Write", [parameterType]);

            if (write == null)
            {
                return;
            }

            EmitExpression(scope, value);
            scope.IL.Emit(OpCodes.Call, write);
        }

        /// <summary>
        /// Looks through conversions the binder inserted purely to satisfy an <c>any</c>
        /// parameter, recovering the expression's real static type.
        /// </summary>
        /// <remarks>
        /// Only conversions whose target is <c>any</c> are unwrapped. A conversion that changes
        /// the value — a numeric widening, or a cast the program wrote — is left alone.
        /// </remarks>
        private static BoundExpression UnwrapConversionToAny(BoundExpression expression)
        {
            while (expression is BoundConversionExpression conversion && conversion.Type == TypeSymbol.Any)
            {
                expression = conversion.Expression;
            }

            return expression;
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
        private void CoerceForStringConcat(MethodBodyScope scope, TypeSymbol operandType)
        {
            if (operandType == TypeSymbol.String)
            {
                return;
            }

            // A statically known type converts to string without boxing, which also lets the
            // concatenation itself use Concat(string, string) rather than the object overload.
            if (RuntimeOverloadParameter(operandType) != null)
            {
                EmitStringConversion(scope, operandType);
                return;
            }

            var typeReference = GetTypeReference(operandType);

            if (typeReference.IsValueType)
            {
                scope.IL.Emit(OpCodes.Box, typeReference);
            }
        }

        /// <summary>
        /// Whether both operands of a string concatenation can be converted to <c>string</c>
        /// without boxing, allowing <c>Concat(string, string)</c> instead of the object overload.
        /// </summary>
        /// <summary>
        /// <c>String.Concat(string, string)</c>, resolved once and reused.
        /// </summary>
        /// <remarks>
        /// Preferred over the <c>object</c> overload wherever both operands are already strings:
        /// it skips the boxing and also the runtime <c>ToString</c> dispatch <c>Concat</c> would
        /// otherwise perform on each argument.
        /// </remarks>
        private MethodReference StringConcatOfStrings() =>
            _stringConcatStringsReference ??=
                ResolveMethod("System.String", "Concat", ["System.String", "System.String"])
                ?? _stringConcatReference;

        private bool CanConcatAsStrings(BoundBinaryExpression node) =>
            (node.Left.Type == TypeSymbol.String || RuntimeOverloadParameter(node.Left.Type) != null)
            && (node.Right.Type == TypeSymbol.String || RuntimeOverloadParameter(node.Right.Type) != null);

        private void EmitBinaryExpression(MethodBodyScope scope, BoundBinaryExpression node)
        {
            var isStringConcatenation =
                node.Op.Kind == BoundBinaryOperatorKind.Addition && node.Op.Type == TypeSymbol.String;

            // When both sides can become strings without boxing, each is converted in place and
            // the concatenation takes Concat(string, string) — no allocation beyond the result.
            var concatAsStrings = isStringConcatenation && CanConcatAsStrings(node);

            EmitExpression(scope, node.Left);
            if (isStringConcatenation)
            {
                CoerceForStringConcat(scope, node.Left.Type);
            }

            EmitExpression(scope, node.Right);
            if (isStringConcatenation)
            {
                CoerceForStringConcat(scope, node.Right.Type);
            }

            if (node.Op.Kind == BoundBinaryOperatorKind.Addition)
            {
                if (node.Op.Type == TypeSymbol.String)
                {
                    scope.IL.Emit(OpCodes.Call, concatAsStrings ? StringConcatOfStrings() : _stringConcatReference);
                }
                else
                {
                    scope.IL.Emit(OpCodes.Add);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Subtraction)
            {
                scope.IL.Emit(OpCodes.Sub);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Multiplication)
            {
                scope.IL.Emit(OpCodes.Mul);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Division)
            {
                scope.IL.Emit(OpCodes.Div);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LogicalAnd)
            {
                scope.IL.Emit(OpCodes.And);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LogicalOr)
            {
                scope.IL.Emit(OpCodes.Or);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Equals)
            {
                if (node.Left.Type == TypeSymbol.String || node.Right.Type == TypeSymbol.String)
                {
                    var eqMethod = ResolveMethod("System.String", "op_Equality", new[] { "System.String", "System.String" });
                    if (eqMethod != null)
                        scope.IL.Emit(OpCodes.Call, eqMethod);
                    else
                        scope.IL.Emit(OpCodes.Ceq);
                }
                else
                {
                    scope.IL.Emit(OpCodes.Ceq);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.NotEquals)
            {
                if (node.Left.Type == TypeSymbol.String || node.Right.Type == TypeSymbol.String)
                {
                    var neqMethod = ResolveMethod("System.String", "op_Inequality", new[] { "System.String", "System.String" });
                    if (neqMethod != null)
                        scope.IL.Emit(OpCodes.Call, neqMethod);
                    else
                    {
                        scope.IL.Emit(OpCodes.Ceq);
                        scope.IL.Emit(OpCodes.Ldc_I4_0);
                        scope.IL.Emit(OpCodes.Ceq);
                    }
                }
                else
                {
                    scope.IL.Emit(OpCodes.Ceq);
                    scope.IL.Emit(OpCodes.Ldc_I4_0);
                    scope.IL.Emit(OpCodes.Ceq);
                }
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LessThan)
            {
                scope.IL.Emit(OpCodes.Clt);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.LessEqual)
            {
                scope.IL.Emit(OpCodes.Cgt);
                scope.IL.Emit(OpCodes.Ldc_I4_0);
                scope.IL.Emit(OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.GreaterThan)
            {
                scope.IL.Emit(OpCodes.Cgt);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.GreaterEqual)
            {
                scope.IL.Emit(OpCodes.Clt);
                scope.IL.Emit(OpCodes.Ldc_I4_0);
                scope.IL.Emit(OpCodes.Ceq);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.Modulo)
            {
                scope.IL.Emit(OpCodes.Rem);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseAnd)
            {
                scope.IL.Emit(OpCodes.And);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseOr)
            {
                scope.IL.Emit(OpCodes.Or);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseXor)
            {
                scope.IL.Emit(OpCodes.Xor);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseLeftShift)
            {
                scope.IL.Emit(OpCodes.Shl);
            }
            else if (node.Op.Kind == BoundBinaryOperatorKind.BitwiseRightShift)
            {
                scope.IL.Emit(OpCodes.Shr_Un);
            }
            else
            {
                throw new Exception($"Unexpected binary operator {node.Op.Kind}");
            }
        }

        private void EmitUnaryExpression(MethodBodyScope scope, BoundUnaryExpression node)
        {
            EmitExpression(scope, node.Operand);

            if (node.Op.Kind == BoundUnaryOperatorKind.Identity)
            {
                //do nothing
            }
            else if (node.Op.Kind == BoundUnaryOperatorKind.Negation)
            {
                scope.IL.Emit(OpCodes.Neg);
            }
            else if (node.Op.Kind == BoundUnaryOperatorKind.LogicalNegation)
            {
                scope.IL.Emit(OpCodes.Ldc_I4_0);
                scope.IL.Emit(OpCodes.Ceq);
            }
            else
            {
                throw new Exception($"Unexpected unary operator {node.Op.Kind}");
            }
        }

        private void EmitAssignmentExpression(MethodBodyScope scope, BoundAssignmentExpression node)
        {
            EmitExpression(scope, node.Expression);
            scope.IL.Emit(OpCodes.Dup);

            if (node.Variable is ParameterSymbol parameter)
            {
                scope.IL.Emit(OpCodes.Starg, scope.Method.Parameters[parameter.Ordinal]);
            }
            else
            {
                var variableDefinition = scope.GetLocal(node.Variable);
                scope.IL.Emit(OpCodes.Stloc, variableDefinition);
            }
        }

        private void EmitVariableExpression(MethodBodyScope scope, BoundVariableExpression node)
        {
            if (node.Variable is ParameterSymbol parameter)
            {
                scope.IL.Emit(OpCodes.Ldarg, scope.Method.Parameters[parameter.Ordinal]);
            }
            else
            {
                var variableDefinition = scope.GetLocal(node.Variable);

                scope.IL.Emit(OpCodes.Ldloc, variableDefinition);
            }
        }

        private void EmitLiteralExpression(MethodBodyScope scope, BoundLiteralExpression node)
        {
            if (node.Type == TypeSymbol.Bool)
            {
                var value = (bool)node.Value;

                var instruction = value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;

                scope.IL.Emit(instruction);
            }
            else if (node.Type == TypeSymbol.Int
            || node.Type == TypeSymbol.UInt32
            || node.Type == TypeSymbol.Int16
            || node.Type == TypeSymbol.UInt16
            || node.Type == TypeSymbol.UInt8)
            {
                var value = Convert.ToInt32(node.Value);

                scope.IL.Emit(OpCodes.Ldc_I4, value);
            }
            else if (node.Type == TypeSymbol.Int8)//Chosen the more efficient instruction
            {
                var value = (sbyte)node.Value;

                scope.IL.Emit(OpCodes.Ldc_I4_S, value);
            }
            else if (node.Type == TypeSymbol.Int64
            || node.Type == TypeSymbol.UInt64)
            {
                var value = (long)node.Value;

                scope.IL.Emit(OpCodes.Ldc_I8, value);
            }
            else if (node.Type == TypeSymbol.Float32)
            {
                scope.IL.Emit(OpCodes.Ldc_R4, (float)node.Value);
            }
            else if (node.Type == TypeSymbol.Float64 || node.Type == TypeSymbol.Float)
            {
                scope.IL.Emit(OpCodes.Ldc_R8, (double)node.Value);
            }
            else if (node.Type == TypeSymbol.String)
            {
                var value = (string)node.Value;

                scope.IL.Emit(OpCodes.Ldstr, value);
            }
            else
            {
                throw new NotImplementedException($"Unexpected literal type: {node.Type}");
            }
        }

        private void EmitStructCreationExpression(MethodBodyScope scope, BoundStructCreationExpression node)
        {
            var structSymbol = node.StructType;
            var typeDef = _structTypes[structSymbol.Name];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);

            var localVar = scope.DeclareTemporary(typeRef);

            foreach (var field in structSymbol.Fields)
            {
                scope.IL.Emit(OpCodes.Ldloca, localVar);

                var fieldIndex = structSymbol.Fields.IndexOf(field);
                var fieldValue = node.FieldValues[fieldIndex];
                EmitExpression(scope, fieldValue);

                var fieldType = GetTypeReference(field.Type);

                // Box only when storing a value type into an `any` field, which is
                // System.Object at the IL level. The condition here used to be inverted —
                // `field.Type != any && !fieldType.IsValueType` — which boxed *reference* types
                // into non-`any` fields, emitting `box string` and `box Object[]`.
                if (field.Type == TypeSymbol.Any && GetTypeReference(fieldValue.Type).IsValueType)
                {
                    scope.IL.Emit(OpCodes.Box, GetTypeReference(fieldValue.Type));
                }

                var fieldRef = new FieldReference(field.Name, fieldType);
                fieldRef.DeclaringType = typeRef;
                scope.IL.Emit(OpCodes.Stfld, fieldRef);
            }

            scope.IL.Emit(OpCodes.Ldloc, localVar);
        }

        private void EmitFieldAccessExpression(MethodBodyScope scope, BoundFieldAccessExpression node)
        {
            EmitExpression(scope, node.Expression);

            var structSymbol = (StructSymbol)node.Expression.Type;
            var typeDef = _structTypes[structSymbol.Name];
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);
            var fieldType = GetTypeReference(node.Field.Type);
            var fieldRef = new FieldReference(node.FieldName, fieldType);
            fieldRef.DeclaringType = typeRef;

            scope.IL.Emit(OpCodes.Ldfld, fieldRef);

            if (node.Type == TypeSymbol.Any && fieldType.IsValueType)
            {
                scope.IL.Emit(OpCodes.Box, fieldType);
            }
        }

        private void EmitFieldAssignmentExpression(MethodBodyScope scope, BoundFieldAssignmentExpression node)
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
                    scope.IL.Emit(OpCodes.Ldarga, scope.Method.Parameters[paramSym.Ordinal]);
                }
                else
                {
                    varDef = scope.GetLocal(varExpr.Variable);
                    scope.IL.Emit(OpCodes.Ldloca, varDef);
                }

                EmitExpression(scope, node.Value);
                scope.IL.Emit(OpCodes.Dup);

                var tempVar = scope.DeclareTemporary(fieldType);
                scope.IL.Emit(OpCodes.Stloc, tempVar);

                scope.IL.Emit(OpCodes.Stfld, fieldRef);

                scope.IL.Emit(OpCodes.Ldloc, tempVar);
            }
            else
            {
                EmitExpression(scope, node.Expression);
                scope.IL.Emit(OpCodes.Dup);
                EmitExpression(scope, node.Value);
                scope.IL.Emit(OpCodes.Stfld, fieldRef);
            }
        }
    }
}
