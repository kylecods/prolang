using BenchmarkDotNet.Attributes;
using ProLang.Compiler;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CompilerBenchmarks
{
    private string _simpleProgram = "";
    private string _stringProcessingProgram = "";
    private string _structProgram = "";
    private string _largeProgram = "";
    private string _complexExpressions = "";

    [GlobalSetup]
    public void Setup()
    {
        _simpleProgram = GenerateSimpleProgram();
        _stringProcessingProgram = GenerateStringProcessingProgram();
        _structProgram = GenerateStructProgram();
        _largeProgram = GenerateLargeProgram();
        _complexExpressions = GenerateComplexExpressions();
    }

    // ==== Full Compilation Pipeline Benchmarks ====

    [Benchmark(Baseline = true, Description = "Full Pipeline: Simple Program")]
    public void FullCompile_Simple()
    {
        CompileProgram(_simpleProgram, "Simple");
    }

    [Benchmark(Description = "Full Pipeline: String Processing")]
    public void FullCompile_StringProcessing()
    {
        CompileProgram(_stringProcessingProgram, "StringProc");
    }

    [Benchmark(Description = "Full Pipeline: Struct Heavy")]
    public void FullCompile_StructHeavy()
    {
        CompileProgram(_structProgram, "Structs");
    }

    [Benchmark(Description = "Full Pipeline: Large Program")]
    public void FullCompile_Large()
    {
        CompileProgram(_largeProgram, "Large");
    }

    [Benchmark(Description = "Full Pipeline: Complex Expressions")]
    public void FullCompile_ComplexExpressions()
    {
        CompileProgram(_complexExpressions, "Complex");
    }

    // ==== Phase-by-Phase Benchmarks (Lexer + Parser) ====

    [Benchmark(Description = "Lexing + Parsing: Simple")]
    public void ParseOnly_Simple()
    {
        var syntaxTree = SyntaxTree.Parse(_simpleProgram);
    }

    [Benchmark(Description = "Lexing + Parsing: String Processing")]
    public void ParseOnly_StringProcessing()
    {
        var syntaxTree = SyntaxTree.Parse(_stringProcessingProgram);
    }

    [Benchmark(Description = "Lexing + Parsing: Struct Heavy")]
    public void ParseOnly_StructHeavy()
    {
        var syntaxTree = SyntaxTree.Parse(_structProgram);
    }

    [Benchmark(Description = "Lexing + Parsing: Large Program")]
    public void ParseOnly_Large()
    {
        var syntaxTree = SyntaxTree.Parse(_largeProgram);
    }

    [Benchmark(Description = "Lexing + Parsing: Complex Expressions")]
    public void ParseOnly_ComplexExpressions()
    {
        var syntaxTree = SyntaxTree.Parse(_complexExpressions);
    }

    // ==== Phase-by-Phase Benchmarks (Binding + Lowering) ====

    [Benchmark(Description = "Binding + Lowering: Simple")]
    public void BindOnly_Simple()
    {
        var syntaxTree = SyntaxTree.Parse(_simpleProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
    }

    [Benchmark(Description = "Binding + Lowering: String Processing")]
    public void BindOnly_StringProcessing()
    {
        var syntaxTree = SyntaxTree.Parse(_stringProcessingProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
    }

    [Benchmark(Description = "Binding + Lowering: Struct Heavy")]
    public void BindOnly_StructHeavy()
    {
        var syntaxTree = SyntaxTree.Parse(_structProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
    }

    [Benchmark(Description = "Binding + Lowering: Large Program")]
    public void BindOnly_Large()
    {
        var syntaxTree = SyntaxTree.Parse(_largeProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
    }

    [Benchmark(Description = "Binding + Lowering: Complex Expressions")]
    public void BindOnly_ComplexExpressions()
    {
        var syntaxTree = SyntaxTree.Parse(_complexExpressions);
        var compilation = ProLangCompilation.Create(syntaxTree);
    }

    // ==== Phase-by-Phase Benchmarks (Code Generation) ====

    [Benchmark(Description = "Emission: Simple")]
    public void EmitOnly_Simple()
    {
        var syntaxTree = SyntaxTree.Parse(_simpleProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        compilation.Emit("Simple", Array.Empty<string>(), "simple.dll");
    }

    [Benchmark(Description = "Emission: String Processing")]
    public void EmitOnly_StringProcessing()
    {
        var syntaxTree = SyntaxTree.Parse(_stringProcessingProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        compilation.Emit("StringProc", Array.Empty<string>(), "stringproc.dll");
    }

    [Benchmark(Description = "Emission: Struct Heavy")]
    public void EmitOnly_StructHeavy()
    {
        var syntaxTree = SyntaxTree.Parse(_structProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        compilation.Emit("Structs", Array.Empty<string>(), "structs.dll");
    }

    [Benchmark(Description = "Emission: Large Program")]
    public void EmitOnly_Large()
    {
        var syntaxTree = SyntaxTree.Parse(_largeProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        compilation.Emit("Large", Array.Empty<string>(), "large.dll");
    }

    [Benchmark(Description = "Emission: Complex Expressions")]
    public void EmitOnly_ComplexExpressions()
    {
        var syntaxTree = SyntaxTree.Parse(_complexExpressions);
        var compilation = ProLangCompilation.Create(syntaxTree);
        compilation.Emit("Complex", Array.Empty<string>(), "complex.dll");
    }

    // ==== Helper Methods ====

    private void CompileProgram(string source, string moduleName)
    {
        var syntaxTree = SyntaxTree.Parse(source);
        var compilation = ProLangCompilation.Create(syntaxTree);
        compilation.Emit(moduleName, Array.Empty<string>(), moduleName.ToLower() + ".dll");
    }

    private string GenerateSimpleProgram()
    {
        return """
            func main() {
                var x = 5
                var y = 10
                var z = x + y
                print(z)
            }
            """;
    }

    private string GenerateStringProcessingProgram()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("func countChar(text: string, ch: string) : int {");
        sb.AppendLine("    var count = 0");
        sb.AppendLine("    var i = 0");
        sb.AppendLine("    while(i < text.length()) {");
        sb.AppendLine("        if(text.charAt(i) == ch) {");
        sb.AppendLine("            count = count + 1");
        sb.AppendLine("        }");
        sb.AppendLine("        i = i + 1");
        sb.AppendLine("    }");
        sb.AppendLine("    return count");
        sb.AppendLine("}");
        sb.AppendLine("");

        for (int i = 0; i < 8; i++)
        {
            sb.AppendLine($"func process{i}(text: string) : int {{");
            sb.AppendLine($"    return countChar(text, \"a\") + countChar(text, \"b\")");
            sb.AppendLine("}");
        }

        sb.AppendLine("");
        sb.AppendLine("func main() {");
        sb.AppendLine("    var input = \"hello world hello\"");
        sb.AppendLine("    var count = countChar(input, \"l\")");
        sb.AppendLine("    print(count)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GenerateStructProgram()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("struct Point {");
        sb.AppendLine("    x: int;");
        sb.AppendLine("    y: int;");
        sb.AppendLine("}");
        sb.AppendLine("");

        sb.AppendLine("struct Rectangle {");
        sb.AppendLine("    topLeft: Point;");
        sb.AppendLine("    bottomRight: Point;");
        sb.AppendLine("}");
        sb.AppendLine("");

        for (int i = 0; i < 5; i++)
        {
            sb.AppendLine($"struct Data{i} {{");
            sb.AppendLine("    value: int;");
            sb.AppendLine("    name: string;");
            sb.AppendLine("    flag: bool;");
            sb.AppendLine("}");
            sb.AppendLine("");
        }

        sb.AppendLine("func createPoint(px: int, py: int) : Point {");
        sb.AppendLine("    return Point { x: px, y: py }");
        sb.AppendLine("}");
        sb.AppendLine("");

        sb.AppendLine("func main() {");
        sb.AppendLine("    var p = createPoint(10, 20)");
        sb.AppendLine("    var rect = Rectangle {");
        sb.AppendLine("        topLeft: Point { x: 0, y: 0 },");
        sb.AppendLine("        bottomRight: p");
        sb.AppendLine("    }");
        sb.AppendLine("    print(rect.topLeft.x)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GenerateLargeProgram()
    {
        var sb = new System.Text.StringBuilder();

        // Generate 20 functions with various complexity
        for (int i = 0; i < 20; i++)
        {
            sb.AppendLine("func func" + i + "(a: int, b: int, c: int) : int {");
            sb.AppendLine("    var result = a + b * c");
            sb.AppendLine("    result = result - (a / 2)");
            sb.AppendLine("    if (result > 0) {");
            sb.AppendLine("        result = result * 2");
            sb.AppendLine("    }");
            sb.AppendLine("    return result");
            sb.AppendLine("}");
            sb.AppendLine("");
        }

        // Generate 10 structs
        for (int i = 0; i < 10; i++)
        {
            sb.AppendLine("struct Struct" + i + " {");
            sb.AppendLine("    field1: int;");
            sb.AppendLine("    field2: string;");
            sb.AppendLine("    field3: bool;");
            sb.AppendLine("    field4: int;");
            sb.AppendLine("}");
            sb.AppendLine("");
        }

        sb.AppendLine("func main() {");
        sb.AppendLine("    var x = 100");
        for (int i = 0; i < 10; i++)
        {
            sb.AppendLine("    x = func" + i + "(x, 5, 3)");
        }
        sb.AppendLine("    print(x)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GenerateComplexExpressions()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("func evaluate(a: int, b: int, c: int) : int {");
        sb.AppendLine("    return a + b * c - (a / b) + (c * a) - (b / c) + (a * b * c)");
        sb.AppendLine("}");
        sb.AppendLine("");

        sb.AppendLine("func complexLogic(x: int) : int {");
        sb.AppendLine("    var result = x");
        sb.AppendLine("    if (x > 10 && x < 100 || x == 50) {");
        sb.AppendLine("        result = result + 100");
        sb.AppendLine("    } elif (x > 100 && x < 200) {");
        sb.AppendLine("        result = result - 50");
        sb.AppendLine("    } else {");
        sb.AppendLine("        result = result * 2");
        sb.AppendLine("    }");
        sb.AppendLine("    return result");
        sb.AppendLine("}");
        sb.AppendLine("");

        // Generate nested expressions
        for (int i = 0; i < 5; i++)
        {
            sb.AppendLine("func nested" + i + "(x: int) : int {");
            sb.AppendLine("    return (x + 1) * (x - 1) + (x * x) - (x / 2) * (x + 2)");
            sb.AppendLine("}");
            sb.AppendLine("");
        }

        sb.AppendLine("func main() {");
        sb.AppendLine("    var result = evaluate(5, 10, 15)");
        sb.AppendLine("    result = complexLogic(50)");
        sb.AppendLine("    print(result)");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
