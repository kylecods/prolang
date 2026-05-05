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

    [GlobalSetup]
    public void Setup()
    {
        // Simple baseline - basic ProLang program
        _simpleProgram = """
            func main() {
                var x = 5
                var y = 10
                var z = x + y
                print(z)
            }
            """;

        // String processing program (allocates heavily with string operations)
        _stringProcessingProgram = GenerateStringProcessingProgram();

        // Struct-heavy program (tests type resolution and struct creation)
        _structProgram = GenerateStructProgram();
    }

    [Benchmark(Baseline = true)]
    public void SimpleCompilation()
    {
        var syntaxTree = SyntaxTree.Parse(_simpleProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        var diagnostics = compilation.Emit("Simple", Array.Empty<string>(), "simple.dll");
    }

    [Benchmark]
    public void StringProcessingProgram()
    {
        var syntaxTree = SyntaxTree.Parse(_stringProcessingProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        var diagnostics = compilation.Emit("StringProc", Array.Empty<string>(), "stringproc.dll");
    }

    [Benchmark]
    public void StructHeavyProgram()
    {
        var syntaxTree = SyntaxTree.Parse(_structProgram);
        var compilation = ProLangCompilation.Create(syntaxTree);
        var diagnostics = compilation.Emit("Structs", Array.Empty<string>(), "structs.dll");
    }

    private string GenerateStringProcessingProgram()
    {
        // Real ProLang code: string processing like the JSON parser example
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

        // Generate multiple similar functions to test compilation under load
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
        // Real ProLang code: structs like JsonValue
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

        // Generate multiple struct definitions
        for (int i = 0; i < 5; i++)
        {
            sb.AppendLine($"struct Data{i} {{");
            sb.AppendLine($"    value: int;");
            sb.AppendLine($"    name: string;");
            sb.AppendLine($"    flag: bool;");
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
}
