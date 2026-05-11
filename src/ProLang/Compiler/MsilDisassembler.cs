using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ProLang.Compiler;

internal static class MsilDisassembler
{
    // Methods emitted by the runtime infrastructure that are not user code
    private static readonly HashSet<string> InfrastructureMethods = new(StringComparer.Ordinal)
    {
        "__InitializeOutput",
        "__AppendToOutput",
        "__FlushOutput",
    };

    public static void Disassemble(string dllPath, TextWriter output)
    {
        AssemblyDefinition assembly;
        try
        {
            assembly = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters { ReadSymbols = false });
        }
        catch (Exception ex)
        {
            output.WriteLine($"error: could not read assembly '{dllPath}': {ex.Message}");
            return;
        }

        output.WriteLine($"=== ProLang MSIL Disassembly: {Path.GetFileName(dllPath)} ===");
        output.WriteLine($"Module : {assembly.Name.Name}  v{assembly.Name.Version}");
        output.WriteLine();

        foreach (var module in assembly.Modules)
        {
            var structs = module.Types
                .Where(t => t.IsValueType && t.Name != "<Module>")
                .OrderBy(t => t.Name)
                .ToList();

            var programType = module.Types.FirstOrDefault(t => t.Name == "Program");

            if (structs.Count > 0)
            {
                output.WriteLine("// ── Value types (structs) ─────────────────────────────");
                foreach (var s in structs)
                    WriteValueType(s, output);
                output.WriteLine();
            }

            if (programType != null)
            {
                var userMethods = programType.Methods
                    .Where(m => !InfrastructureMethods.Contains(m.Name))
                    .OrderBy(m => m.Name)
                    .ToList();

                if (userMethods.Count > 0)
                {
                    output.WriteLine("// ── Methods ───────────────────────────────────────────");
                    foreach (var method in userMethods)
                        WriteMethod(method, assembly, output);
                }
            }
        }
    }

    private static void WriteValueType(TypeDefinition type, TextWriter output)
    {
        output.WriteLine($"valuetype {type.Name}");
        foreach (var field in type.Fields)
        {
            output.WriteLine($"    Field: {MapTypeName(field.FieldType.FullName),-12} {field.Name}");
        }
        output.WriteLine();
    }

    private static void WriteMethod(MethodDefinition method, AssemblyDefinition assembly, TextWriter output)
    {
        bool isEntry = assembly.EntryPoint != null &&
                       method.FullName == assembly.EntryPoint.FullName;

        var returnType = MapTypeName(method.ReturnType.FullName);
        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{MapTypeName(p.ParameterType.FullName)} {p.Name}"));

        output.Write(isEntry ? "[entry] " : "        ");
        output.WriteLine($"{returnType} {method.DeclaringType.Name}::{method.Name}({parameters})");

        if (!method.HasBody)
        {
            output.WriteLine("    (no body)");
            output.WriteLine();
            return;
        }

        var body = method.Body;

        // Local variable table
        if (body.Variables.Count > 0)
        {
            output.WriteLine("  Locals:");
            foreach (var v in body.Variables)
            {
                output.WriteLine($"    [{v.Index}] {MapTypeName(v.VariableType.FullName),-14} V_{v.Index}");
            }
        }

        // Instructions
        output.WriteLine("  Body:");
        foreach (var instr in body.Instructions)
        {
            var operand = FormatOperand(instr);
            output.WriteLine($"    IL_{instr.Offset:X4}  {instr.OpCode.Name,-16} {operand}");
        }

        output.WriteLine();
    }

    private static string FormatOperand(Instruction instr)
    {
        if (instr.Operand == null)
            return string.Empty;

        return instr.Operand switch
        {
            string s        => $"\"{s}\"",
            int i           => i.ToString(),
            long l          => l.ToString(),
            float f         => f.ToString("G"),
            double d        => d.ToString("G"),
            MethodReference mr  => $"{MapTypeName(mr.ReturnType.FullName)} {mr.DeclaringType.Name}::{mr.Name}({string.Join(", ", mr.Parameters.Select(p => MapTypeName(p.ParameterType.FullName)))})",
            FieldReference fr   => $"{MapTypeName(fr.FieldType.FullName)} {fr.DeclaringType.Name}::{fr.Name}",
            TypeReference tr    => MapTypeName(tr.FullName),
            Instruction target  => $"IL_{target.Offset:X4}",
            VariableDefinition v => $"V_{v.Index}",
            ParameterDefinition p => p.Name,
            _ => instr.Operand.ToString() ?? string.Empty
        };
    }

    private static string MapTypeName(string fullName)
    {
        // Strip generic arity suffixes like `1, `2 before matching
        var bare = fullName;
        var tickIdx = bare.IndexOf('`');
        if (tickIdx >= 0) bare = bare[..tickIdx];

        return bare switch
        {
            "System.Int32"   => "int",
            "System.Boolean" => "bool",
            "System.String"  => "string",
            "System.Object"  => "any",
            "System.Void"    => "void",
            "System.Collections.Generic.List"       => "array",
            "System.Collections.Generic.Dictionary" => "map",
            "System.Text.StringBuilder" => "StringBuilder",
            _ => ShortenSystemName(fullName)
        };
    }

    private static string ShortenSystemName(string fullName)
    {
        // Keep generic annotations readable: List`1<System.Int32> → array<int>
        if (fullName.StartsWith("System.Collections.Generic.List`1<"))
        {
            var inner = fullName["System.Collections.Generic.List`1<".Length..^1];
            return $"array<{MapTypeName(inner)}>";
        }
        if (fullName.StartsWith("System.Collections.Generic.Dictionary`2<"))
        {
            var inner = fullName["System.Collections.Generic.Dictionary`2<".Length..^1];
            var comma = inner.IndexOf(',');
            if (comma >= 0)
            {
                var k = MapTypeName(inner[..comma].Trim());
                var v = MapTypeName(inner[(comma + 1)..].Trim());
                return $"map<{k}, {v}>";
            }
        }
        // Strip System. prefix for brevity
        if (fullName.StartsWith("System."))
            return fullName["System.".Length..];
        return fullName;
    }
}
