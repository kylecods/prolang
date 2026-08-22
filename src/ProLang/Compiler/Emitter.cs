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

        /// <summary>Maps ProLang types to Cecil references and owns emitted struct definitions.</summary>
        private readonly TypeEmitter _types;

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


        private readonly AssemblyDefinition _assemblyDefinition;

        private Dictionary<FunctionSymbol, MethodDefinition> _methods = new();



        private TypeDefinition _typeDefinition;

        private readonly EmitOptions _options;

        private static readonly string[] stringArray = ["System.String"];

        /// <summary>Maps the compiler's target kind onto Cecil's.</summary>
        private static ModuleKind ToModuleKind(EmitTargetKind kind) => kind switch
        {
            EmitTargetKind.ConsoleApplication => ModuleKind.Console,
            EmitTargetKind.WindowsApplication => ModuleKind.Windows,
            _ => ModuleKind.Dll,
        };

        private Emitter(string moduleName, string[] references, EmitOptions options)
        {
            _options = options;

            var assemblyName = new AssemblyNameDefinition(moduleName, new Version(1, 0));
            _assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ToModuleKind(options.TargetKind));

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
            _types = new TypeEmitter(_references, _assemblyDefinition.MainModule);
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

        /// <inheritdoc cref="TypeEmitter.GetReference"/>
        private TypeReference GetTypeReference(TypeSymbol type) => _types.GetReference(type);

        /// <summary>
        /// The emitted definition for a struct, emitting it first if it has not been seen.
        /// </summary>
        /// <remarks>
        /// A field access or struct creation can be the first thing that mentions a
        /// monomorphised generic struct, since those are not in <c>BoundProgram.StructTypes</c>.
        /// This used to be an unguarded dictionary indexer, which threw
        /// <see cref="KeyNotFoundException"/> instead of emitting the missing type.
        /// </remarks>
        private TypeDefinition ResolveStructDefinition(StructSymbol structSymbol) =>
            _types.TryGetStruct(structSymbol.Name, out var definition)
                ? definition
                : _types.EmitStruct(structSymbol);

        public static ImmutableArray<Diagnostic> Emit(BoundProgram program, string moduleName, string[] references, string outputPath, EmitOptions? options = null)
        {
            if (program.Diagnostics.Any())
            {
                return program.Diagnostics;
            }

            var emitter = new Emitter(moduleName, references, options ?? EmitOptions.Default);

            return emitter.Emit(program, outputPath);
        }

        /// <summary>
        /// Emits the assembly to a stream instead of a file. Used by tests and benchmarks so that
        /// disk I/O does not sit inside the measured region and temp files are not required.
        /// No <c>.runtimeconfig.json</c> is produced — there is no path to derive it from.
        /// </summary>
        public static ImmutableArray<Diagnostic> Emit(BoundProgram program, string moduleName, string[] references, Stream outputStream, EmitOptions? options = null)
        {
            if (program.Diagnostics.Any())
            {
                return program.Diagnostics;
            }

            var emitter = new Emitter(moduleName, references, options ?? EmitOptions.Default);

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

            // Create the output directory rather than failing with a DirectoryNotFoundException
            // stack trace. `-o bin/app.dll` into a directory that does not exist yet is an
            // ordinary thing to write, not a mistake.
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
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
                _types.EmitStruct(structType);
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
                var entryPoint = _methods[program.MainFunction];
                _assemblyDefinition.EntryPoint = entryPoint;
                ApplyApartmentState(entryPoint);
            }
        }

        /// <summary>
        /// Marks the entry point <c>[STAThread]</c> for programs that will use Windows Desktop.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The CLR reads this attribute off the entry point and sets the main thread's COM
        /// apartment before calling it. Without it the thread is MTA, and the Windows *common*
        /// dialogs — open file, save file, folder browse — are COM objects
        /// (<c>IFileDialog</c>) that require STA. Calling one from an MTA thread does not throw:
        /// it disables the owner window and never returns, so the program looks frozen while
        /// still pumping messages. Ordinary <see cref="System.Windows.Forms.Form.ShowDialog()"/>
        /// and <c>MessageBox</c> are unaffected, which makes the failure look specific to the
        /// file commands rather than to the apartment.
        /// </para>
        /// <para>
        /// Applied when the program targets the windows subsystem or asks for the Windows Desktop
        /// framework — the two ways of saying "this is a GUI program". Console programs are left
        /// MTA, which is the .NET default and what any threading they do will expect.
        /// </para>
        /// <para>
        /// A missing <c>STAThreadAttribute</c> is not fatal. It is only reachable when the BCL
        /// reference set is incomplete, and the program still runs — the file dialogs are what
        /// stop working, which is no worse than before this existed.
        /// </para>
        /// </remarks>
        private void ApplyApartmentState(MethodDefinition entryPoint)
        {
            var wantsSingleThreadedApartment =
                _options.TargetKind == EmitTargetKind.WindowsApplication
                || _options.FrameworkName == EmitOptions.WindowsDesktopFrameworkName;

            if (!wantsSingleThreadedApartment)
            {
                return;
            }

            var constructor = _references.ResolveMethod("System.STAThreadAttribute", ".ctor", []);

            if (constructor == null)
            {
                return;
            }

            entryPoint.CustomAttributes.Add(new CustomAttribute(constructor));
        }

        /// <summary>
        /// Writes the <c>.runtimeconfig.json</c> that lets <c>dotnet &lt;output&gt;.dll</c> resolve a
        /// shared framework. A null path means the caller does not want one (stream emit).
        /// </summary>
        private void WriteRuntimeConfig(string? runtimeConfigPath)
        {
            if (runtimeConfigPath == null)
            {
                return;
            }

            var runtimeConfig = $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "{{_options.FrameworkName}}",
                      "version": "{{_options.FrameworkVersion}}"
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
                case BoundNodeKind.BoundFunctionReference:
                    EmitFunctionReference(scope, (BoundFunctionReference)node);
                    break;
                case BoundNodeKind.BoundIndirectCallExpression:
                    EmitIndirectCallExpression(scope, (BoundIndirectCallExpression)node);
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
                    // stelem.ref stores an *object reference* and is invalid for a value type.
                    // Using it for a struct element produced IL that passed every check the
                    // compiler makes — the evaluation stack stays balanced, so
                    // AssemblyValidityTests could not see it — and then killed the runtime with
                    // "Internal CLR error" the first time the JIT reached the method.
                    if (IsValueTypeElement(elementType))
                    {
                        scope.IL.Emit(OpCodes.Stelem_Any, elementType);
                    }
                    else
                    {
                        scope.IL.Emit(OpCodes.Stelem_Ref);
                    }
                    break;
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
                    // See EmitStelemForType: ldelem.ref is invalid for a value-type element.
                    if (IsValueTypeElement(elementType))
                    {
                        scope.IL.Emit(OpCodes.Ldelem_Any, elementType);
                    }
                    else
                    {
                        scope.IL.Emit(OpCodes.Ldelem_Ref);
                    }
                    break;
            }
        }

        /// <summary>
        /// Whether an array element needs the typed <c>ldelem</c>/<c>stelem</c> form.
        /// </summary>
        /// <remarks>
        /// Reference elements keep the <c>.ref</c> opcodes they have always used, so the IL for
        /// <c>array&lt;string&gt;</c> and friends is unchanged. Only the case that was broken
        /// moves.
        /// <para>
        /// The flag is read from the reference first because a struct declared in the program
        /// being compiled is a <see cref="TypeDefinition"/> and carries it directly.
        /// <see cref="TypeReference.Resolve"/> is the fallback for an imported type, and is
        /// allowed to fail: a type that cannot be resolved is treated as a reference type, which
        /// is what the emitter assumed for every non-primitive before this.
        /// </para>
        /// </remarks>
        private static bool IsValueTypeElement(TypeReference elementType)
        {
            if (elementType.IsValueType)
            {
                return true;
            }

            try
            {
                return elementType.Resolve()?.IsValueType ?? false;
            }
            catch (AssemblyResolutionException)
            {
                return false;
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
                if (toType == TypeSymbol.Any && IsEmittedAsValueType(fromType))
                    scope.IL.Emit(OpCodes.Box, GetTypeReference(fromType));
                else if (fromType == TypeSymbol.Any && IsEmittedAsValueType(toType))
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

            var parameterType = RuntimeOverloads.ParameterTypeFor(fromType);

            // A struct or anything else without a dedicated overload still goes through object.
            if (parameterType == null)
            {
                if (IsEmittedAsValueType(fromType))
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

        /// <summary>
        /// Whether <paramref name="type"/> is emitted as a value type, and so needs boxing to
        /// reach anything typed <see cref="object"/>.
        /// </summary>
        /// <remarks>
        /// The test is on the emitted Cecil type rather than on a list of ProLang type names.
        /// An enumerated list cannot know about types that come from .NET — a
        /// <c>DotNetTypeSymbol</c> for <c>System.Guid</c> is a value type and must be boxed,
        /// but one for <c>StringBuilder</c> must not be.
        /// </remarks>
        private bool IsEmittedAsValueType(TypeSymbol type) => GetTypeReference(type).IsValueType;

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

            if (IntrinsicRegistry.TryGet(node.Function, out var intrinsic))
            {
                intrinsic.Emit(new IntrinsicContext(scope, _references, _diagnostics, GetTypeReference));
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
        /// Emits a function used as a value, as a delegate over a null target.
        /// </summary>
        /// <remarks>
        /// <c>ldnull</c> is the delegate's target, and it is what makes function values free of a
        /// garbage collector's involvement in the C and PSP backends: nothing is captured, so there
        /// is no object for the delegate to keep alive. Every referenced function is a static
        /// method already in <c>_methods</c>, because methods are declared in a pass before any
        /// body is emitted.
        /// </remarks>
        private void EmitFunctionReference(MethodBodyScope scope, BoundFunctionReference node)
        {
            var delegateType = GetTypeReference(node.Type);
            var constructor = _references.GetGenericMethod(delegateType, ".ctor", 2);

            scope.IL.Emit(OpCodes.Ldnull);
            scope.IL.Emit(OpCodes.Ldftn, _methods[node.Function]);
            scope.IL.Emit(OpCodes.Newobj, constructor);
        }

        /// <summary>
        /// Emits a call through a function value: push the delegate, push the arguments, then
        /// <c>Invoke</c>.
        /// </summary>
        private void EmitIndirectCallExpression(MethodBodyScope scope, BoundIndirectCallExpression node)
        {
            EmitExpression(scope, node.Target);

            foreach (var argument in node.Arguments)
            {
                EmitExpression(scope, argument);
            }

            var delegateType = GetTypeReference(node.FunctionType);
            var invoke = _references.GetGenericMethod(delegateType, "Invoke", node.Arguments.Length);

            scope.IL.Emit(OpCodes.Callvirt, invoke);
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

            var value = RuntimeOverloads.UnwrapConversionToAny(argument);
            var parameterType = RuntimeOverloads.ParameterTypeFor(value.Type);

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
            if (RuntimeOverloads.ParameterTypeFor(operandType) != null)
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
            (node.Left.Type == TypeSymbol.String || RuntimeOverloads.ParameterTypeFor(node.Left.Type) != null)
            && (node.Right.Type == TypeSymbol.String || RuntimeOverloads.ParameterTypeFor(node.Right.Type) != null);

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
            var typeDef = ResolveStructDefinition(structSymbol);
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
            var typeDef = ResolveStructDefinition(structSymbol);
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
            var typeDef = ResolveStructDefinition(structSymbol);
            var typeRef = _assemblyDefinition.MainModule.ImportReference(typeDef);
            var fieldType = GetTypeReference(node.Field.Type);
            var fieldRef = new FieldReference(node.FieldName, fieldType);
            fieldRef.DeclaringType = typeRef;

            // A struct is a value type, so the receiver has to be an *address*. Pushing it by
            // value and storing into that — which is what this did for everything except a bare
            // local or parameter — writes into a copy on the evaluation stack that is then
            // thrown away. `arr[i].f = x` and `a.b.c = x` both compiled and silently did nothing.
            if (!TryEmitStructAddress(scope, node.Expression))
            {
                _diagnostics.ReportCannotAssignToTemporaryStructField(node.FieldName);
                return;
            }

            EmitExpression(scope, node.Value);

            // The assignment is an expression, so the assigned value has to be left behind. It is
            // stashed rather than duplicated under the address, because stfld wants the value on
            // top of the receiver and a dup would put it in the wrong order.
            scope.IL.Emit(OpCodes.Dup);
            var assignedValue = scope.DeclareTemporary(fieldType);
            scope.IL.Emit(OpCodes.Stloc, assignedValue);

            scope.IL.Emit(OpCodes.Stfld, fieldRef);
            scope.IL.Emit(OpCodes.Ldloc, assignedValue);
        }

        /// <summary>
        /// Pushes the address of a struct-typed storage location.
        /// </summary>
        /// <returns>
        /// False when <paramref name="expression"/> has no storage location — the result of a
        /// call, for instance — in which case nothing has been emitted.
        /// </returns>
        /// <remarks>
        /// Recursive so that a chain such as <c>outer.inner.leaf = x</c> resolves to
        /// <c>ldloca outer; ldflda inner; stfld leaf</c>: each step narrows the address rather
        /// than copying the struct out of the one before it.
        /// </remarks>
        private bool TryEmitStructAddress(MethodBodyScope scope, BoundExpression expression)
        {
            switch (expression)
            {
                case BoundVariableExpression variable:
                    if (variable.Variable is ParameterSymbol parameter)
                    {
                        scope.IL.Emit(OpCodes.Ldarga, scope.Method.Parameters[parameter.Ordinal]);
                    }
                    else
                    {
                        scope.IL.Emit(OpCodes.Ldloca, scope.GetLocal(variable.Variable));
                    }

                    return true;

                // ldelema yields the address of the element in the array itself, so the write
                // lands in the array rather than in a copy of the element.
                case BoundIndexExpression index when index.Expression.Type.Name == "array":
                    var elementType = index.Expression.Type.TypeArguments.Length > 0
                        ? GetTypeReference(index.Expression.Type.TypeArguments[0])
                        : GetCachedType("System.Object");

                    EmitExpression(scope, index.Expression);
                    EmitExpression(scope, index.Index);
                    scope.IL.Emit(OpCodes.Ldelema, elementType);
                    return true;

                case BoundFieldAccessExpression field when field.Expression.Type is StructSymbol:
                    if (!TryEmitStructAddress(scope, field.Expression))
                    {
                        return false;
                    }

                    scope.IL.Emit(OpCodes.Ldflda, MakeFieldReference(field.Expression.Type, field.FieldName, field.Field.Type));
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Builds a reference to a field of a ProLang struct.</summary>
        private FieldReference MakeFieldReference(TypeSymbol ownerType, string fieldName, TypeSymbol fieldType)
        {
            var typeDefinition = ResolveStructDefinition((StructSymbol)ownerType);

            return new FieldReference(fieldName, GetTypeReference(fieldType))
            {
                DeclaringType = _assemblyDefinition.MainModule.ImportReference(typeDefinition),
            };
        }
    }
}
