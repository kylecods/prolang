using System.Text;

namespace ProLang.Benchmarks;

/// <summary>
/// A named ProLang program that benchmarks compile.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> is what BenchmarkDotNet prints in the parameter column, so it is kept
/// to the short name rather than the source text.
/// </remarks>
public sealed class Workload(string name, string source)
{
    /// <summary>Short identifier shown in benchmark result tables.</summary>
    public string Name { get; } = name;

    /// <summary>The ProLang source text.</summary>
    public string Source { get; } = source;

    public override string ToString() => Name;
}

/// <summary>
/// The programs the compiler benchmarks run against.
/// </summary>
/// <remarks>
/// Each one stresses a different part of the pipeline: <c>Simple</c> establishes the fixed
/// overhead floor, <c>Strings</c> exercises lexer string handling and method resolution,
/// <c>Structs</c> exercises type resolution and field access, <c>Large</c> shows how the pipeline
/// scales, and <c>Expressions</c> stresses operator precedence and expression emission.
/// </remarks>
public static class Workloads
{
    /// <summary>Every workload, in increasing order of size.</summary>
    public static IEnumerable<Workload> All =>
    [
        new("Simple", Simple()),
        new("Strings", StringProcessing()),
        new("Structs", Structs()),
        new("Expressions", ComplexExpressions()),
        new("Large", Large()),
    ];

    private static string Simple() =>
        """
        import "io"

        func main() {
            let x: int = 5
            let y: int = 10
            let z: int = x + y
            print(z)
        }
        """;

    private static string StringProcessing()
    {
        var sb = new StringBuilder();

        sb.AppendLine("import \"io\"");
        sb.AppendLine();
        sb.AppendLine("func countChar(text: string, ch: char) : int {");
        sb.AppendLine("    let count: int = 0");
        sb.AppendLine("    let i: int = 0");
        sb.AppendLine("    while(i < text.length()) {");
        sb.AppendLine("        if(text.charAt(i) == ch) {");
        sb.AppendLine("            count = count + 1");
        sb.AppendLine("        }");
        sb.AppendLine("        i = i + 1");
        sb.AppendLine("    }");
        sb.AppendLine("    return count");
        sb.AppendLine("}");
        sb.AppendLine();

        for (var i = 0; i < 8; i++)
        {
            sb.AppendLine($"func process{i}(text: string) : int {{");
            sb.AppendLine("    return countChar(text, 'a') + countChar(text, 'b')");
            sb.AppendLine("}");
        }

        sb.AppendLine();
        sb.AppendLine("func main() {");
        sb.AppendLine("    let input: string = \"hello world hello\"");
        sb.AppendLine("    let count: int = countChar(input, 'l')");
        sb.AppendLine("    print(count)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Structs()
    {
        var sb = new StringBuilder();

        sb.AppendLine("import \"io\"");
        sb.AppendLine();
        sb.AppendLine("struct Point {");
        sb.AppendLine("    x: int;");
        sb.AppendLine("    y: int;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("struct Rectangle {");
        sb.AppendLine("    topLeft: Point;");
        sb.AppendLine("    bottomRight: Point;");
        sb.AppendLine("}");
        sb.AppendLine();

        for (var i = 0; i < 5; i++)
        {
            sb.AppendLine($"struct Data{i} {{");
            sb.AppendLine("    value: int;");
            sb.AppendLine("    name: string;");
            sb.AppendLine("    flag: bool;");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("func createPoint(px: int, py: int) : Point {");
        sb.AppendLine("    return Point { x: px, y: py }");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("func main() {");
        sb.AppendLine("    let p: Point = createPoint(10, 20)");
        sb.AppendLine("    let rect: Rectangle = Rectangle {");
        sb.AppendLine("        topLeft: Point { x: 0, y: 0 },");
        sb.AppendLine("        bottomRight: p");
        sb.AppendLine("    }");
        sb.AppendLine("    print(rect.topLeft.x)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Large()
    {
        var sb = new StringBuilder();

        sb.AppendLine("import \"io\"");
        sb.AppendLine();

        for (var i = 0; i < 20; i++)
        {
            sb.AppendLine($"func func{i}(a: int, b: int, c: int) : int {{");
            sb.AppendLine("    let result: int = a + b * c");
            sb.AppendLine("    result = result - (a / 2)");
            sb.AppendLine("    if (result > 0) {");
            sb.AppendLine("        result = result * 2");
            sb.AppendLine("    }");
            sb.AppendLine("    return result");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        for (var i = 0; i < 10; i++)
        {
            sb.AppendLine($"struct Struct{i} {{");
            sb.AppendLine("    field1: int;");
            sb.AppendLine("    field2: string;");
            sb.AppendLine("    field3: bool;");
            sb.AppendLine("    field4: int;");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("func main() {");
        sb.AppendLine("    let x: int = 100");
        for (var i = 0; i < 10; i++)
        {
            sb.AppendLine($"    x = func{i}(x, 5, 3)");
        }
        sb.AppendLine("    print(x)");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string ComplexExpressions()
    {
        var sb = new StringBuilder();

        sb.AppendLine("import \"io\"");
        sb.AppendLine();

        sb.AppendLine("func evaluate(a: int, b: int, c: int) : int {");
        sb.AppendLine("    return a + b * c - (a / b) + (c * a) - (b / c) + (a * b * c)");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("func complexLogic(x: int) : int {");
        sb.AppendLine("    let result: int = x");
        sb.AppendLine("    if (x > 10 && x < 100 || x == 50) {");
        sb.AppendLine("        result = result + 100");
        sb.AppendLine("    } elif (x > 100 && x < 200) {");
        sb.AppendLine("        result = result - 50");
        sb.AppendLine("    } else {");
        sb.AppendLine("        result = result * 2");
        sb.AppendLine("    }");
        sb.AppendLine("    return result");
        sb.AppendLine("}");
        sb.AppendLine();

        for (var i = 0; i < 5; i++)
        {
            sb.AppendLine($"func nested{i}(x: int) : int {{");
            sb.AppendLine("    return (x + 1) * (x - 1) + (x * x) - (x / 2) * (x + 2)");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("func main() {");
        sb.AppendLine("    let result: int = evaluate(5, 10, 15)");
        sb.AppendLine("    result = complexLogic(50)");
        sb.AppendLine("    print(result)");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
