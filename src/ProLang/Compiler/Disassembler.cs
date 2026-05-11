using System.CodeDom.Compiler;
using ProLang.Intermediate;
using ProLang.Symbols;
using ProLang.Text;

namespace ProLang.Compiler;

internal static class Disassembler
{
    public static void Disassemble(BoundProgram program, TextWriter output)
    {
        var writer = output is IndentedTextWriter iw ? iw : new IndentedTextWriter(output);

        writer.WriteLine("=== ProLang IR Disassembly ===");
        writer.WriteLine();

        // Collect all structs and functions walking the Previous chain
        var allStructs = new List<StructSymbol>();
        var allFunctions = new List<(FunctionSymbol Symbol, BoundBlockStatement Body)>();
        var seenStructs = new HashSet<string>();
        var seenFunctions = new HashSet<string>();

        var current = program;
        while (current != null)
        {
            foreach (var s in current.StructTypes)
            {
                if (seenStructs.Add(s.Name))
                    allStructs.Add(s);
            }

            foreach (var (fn, body) in current.Functions)
            {
                if (seenFunctions.Add(fn.Name))
                    allFunctions.Add((fn, body));
            }

            current = current.Previous;
        }

        allStructs.Reverse();
        allFunctions.Reverse();

        // Structs
        if (allStructs.Count > 0)
        {
            writer.WriteKeyword("// Structs");
            writer.WriteLine();
            foreach (var s in allStructs)
                WriteStruct(s, writer);
            writer.WriteLine();
        }

        // Functions
        if (allFunctions.Count > 0)
        {
            writer.WriteKeyword("// Functions");
            writer.WriteLine();
            foreach (var (fn, body) in allFunctions)
            {
                WriteFunction(fn, body, program.MainFunction, writer);
                writer.WriteLine();
            }
        }
    }

    private static void WriteStruct(StructSymbol s, IndentedTextWriter writer)
    {
        writer.WriteKeyword("struct ");
        writer.WriteIdentifier(s.Name);

        if (s.TypeParameters.Length > 0)
        {
            writer.WritePunctuation("<");
            for (int i = 0; i < s.TypeParameters.Length; i++)
            {
                if (i > 0) writer.WritePunctuation(", ");
                writer.WriteIdentifier(s.TypeParameters[i].Name);
            }
            writer.WritePunctuation(">");
        }

        writer.WriteLine(" {");
        writer.Indent++;

        foreach (var field in s.Fields)
        {
            writer.WriteIdentifier(field.Name);
            writer.WritePunctuation(": ");
            writer.WriteIdentifier(field.Type.ToString());
            writer.WriteLine();
        }

        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void WriteFunction(FunctionSymbol fn, BoundBlockStatement body, FunctionSymbol? mainFn, IndentedTextWriter writer)
    {
        bool isMain = mainFn != null && fn.Name == mainFn.Name;
        if (isMain)
        {
            writer.WriteKeyword("// [entry point]");
            writer.WriteLine();
        }

        // signature
        writer.WriteKeyword("func ");
        writer.WriteIdentifier(fn.Name);
        writer.WritePunctuation("(");

        for (int i = 0; i < fn.Parameters.Length; i++)
        {
            if (i > 0) writer.WritePunctuation(", ");
            var p = fn.Parameters[i];
            writer.WriteIdentifier(p.Name);
            writer.WritePunctuation(": ");
            writer.WriteIdentifier(p.Type.ToString());
        }

        writer.WritePunctuation(")");

        if (fn.Type != TypeSymbol.Void)
        {
            writer.WritePunctuation(" : ");
            writer.WriteIdentifier(fn.Type.ToString());
        }

        writer.WriteLine();

        body.WriteTo(writer);
    }
}
