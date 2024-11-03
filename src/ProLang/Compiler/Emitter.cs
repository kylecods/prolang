﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using ProLang.Intermediate;
using ProLang.Parse;
using ProLang.Symbols;
using System.Collections.Immutable;

namespace ProLang.Compiler
{
    internal static class Emitter
    {
        public static ImmutableArray<Diagnostic> Emit(BoundProgram program, string moduleName, string[] references, string outputPath)
        {
            if (program.Diagnostics.Any())
            {
                return program.Diagnostics;
            }

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
                    result.ReportInvalidReference(reference);
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

            var assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ModuleKind.Console);

            var knownTypes = new Dictionary<TypeSymbol, TypeReference?>();

            foreach (var (type, metaDataName) in builtInTypes)
            {
                var typeReference = ResolveType(type.Name, metaDataName);

                knownTypes.Add(type, typeReference);
            }

            TypeReference? ResolveType(string proLangName, string metaDataName)
            {
                var foundTypes = assemblies.SelectMany(a => a.Modules)
                                            .SelectMany(a => a.Types)
                                            .Where(t => t.FullName == metaDataName)
                                            .ToArray();

                if (foundTypes.Length == 1)
                {
                    var typeReference = assemblyDefinition.MainModule.ImportReference(foundTypes[0]);

                    return typeReference;
                }
                else if (foundTypes.Length == 0)
                {
                    result.ReportRequiredTypeNotFound(proLangName, metaDataName);
                }
                else
                {
                    result.ReportRequiredTypeAmbiguous(proLangName, metaDataName, foundTypes);
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

                        return assemblyDefinition.MainModule.ImportReference(method);
                    }

                    result.ReportRequiredMethodNotFound(typeName, methodName, parameterTypeNames);

                    return null;
                }
                else if (foundTypes.Length == 0)
                {
                    result.ReportRequiredTypeNotFound(null, typeName);
                }
                else
                {
                    result.ReportRequiredTypeAmbiguous(null, typeName, foundTypes);
                }

                return null;
            }

            var consoleWriteLineReference = ResolveMethod("System.Console", "WriteLine",
                new[] { "System.String" });

            if (result.Any())
            {
                return result.ToImmutableArray();
            }

            var objectType = knownTypes[TypeSymbol.Any];
            var typeDefinition = new TypeDefinition("", "Program",
                TypeAttributes.Abstract | TypeAttributes.Sealed, objectType);

            assemblyDefinition.MainModule.Types.Add(typeDefinition);

            var voidType = knownTypes[TypeSymbol.Void];

            var mainMethod = new MethodDefinition("Main", MethodAttributes.Static | MethodAttributes.Private, voidType);

            typeDefinition.Methods.Add(mainMethod);

            var ilProcessor = mainMethod.Body.GetILProcessor();

            ilProcessor.Emit(OpCodes.Ldstr, "Hello world from ProLang");

            ilProcessor.Emit(OpCodes.Call, consoleWriteLineReference);
            ilProcessor.Emit(OpCodes.Ret);

            assemblyDefinition.EntryPoint = mainMethod;

            assemblyDefinition.Write(outputPath);

            return result.ToImmutableArray();
        }
    }
}
