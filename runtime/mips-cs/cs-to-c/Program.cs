using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

// Deliberately small, fail-closed source backend for the portable unsafe C# core.
// Roslyn still type-checks the same source as ordinary C#. Unsupported syntax is an error.
static class Program
{
    static SemanticModel model = null!;
    static string Name(ISymbol s) => s.ContainingType.Name + "_" + s.Name;
    static string Type(TypeSyntax t) => t switch
    {
        PointerTypeSyntax p => Type(p.ElementType) + "*",
        PredefinedTypeSyntax p => p.Keyword.Text switch { "int" => "int32_t", "uint" => "uint32_t", "long" => "int64_t", "ulong" => "uint64_t", "byte" => "uint8_t", "sbyte" => "int8_t", "short" => "int16_t", "ushort" or "char" => "uint16_t", "bool" => "bool", "string" => "const char*", "void" => "void", _ => throw Error(t) },
        IdentifierNameSyntax i => i.Identifier.Text switch { "nint" => "intptr_t", "nuint" => "uintptr_t", _ => i.Identifier.Text },
        _ => throw Error(t)
    };
    static Exception Error(SyntaxNode n) => new NotSupportedException($"{n.SyntaxTree.FilePath}:{n.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: unsupported {n.Kind()}: {n}");
    static string Id(IdentifierNameSyntax i)
    {
        var s = model.GetSymbolInfo(i).Symbol;
        return s is IFieldSymbol { IsStatic: true } or IMethodSymbol { IsStatic: true } ? Name(s) : i.Identifier.Text;
    }
    static string Expr(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax i => Id(i),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.DefaultLiteralExpression) => "(" + model.GetTypeInfo(l).ConvertedType!.Name + "){0}",
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.NumericLiteralExpression) => l.Token.Text.Replace("UL", "ULL").Replace("ul", "ULL"),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.CharacterLiteralExpression) => ((int)(char)l.Token.Value!).ToString(),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.NullLiteralExpression) => "NULL",
        LiteralExpressionSyntax l => l.Token.Text,
        BinaryExpressionSyntax b => "(" + Expr(b.Left) + " " + b.OperatorToken.Text + " " + Expr(b.Right) + ")",
        AssignmentExpressionSyntax a => Expr(a.Left) + " " + a.OperatorToken.Text + " " + Expr(a.Right),
        PrefixUnaryExpressionSyntax p => "(" + p.OperatorToken.Text + Expr(p.Operand) + ")",
        PostfixUnaryExpressionSyntax p => "(" + Expr(p.Operand) + p.OperatorToken.Text + ")",
        ParenthesizedExpressionSyntax p => "(" + Expr(p.Expression) + ")",
        CastExpressionSyntax c => "((" + Type(c.Type) + ")" + Expr(c.Expression) + ")",
        CheckedExpressionSyntax c => Expr(c.Expression),
        DefaultExpressionSyntax d => "(" + Type(d.Type) + "){0}",
        SizeOfExpressionSyntax s => "sizeof(" + Type(s.Type) + ")",
        ConditionalExpressionSyntax c => "(" + Expr(c.Condition) + " ? " + Expr(c.WhenTrue) + " : " + Expr(c.WhenFalse) + ")",
        ElementAccessExpressionSyntax a => Expr(a.Expression) + "[" + string.Join(",", a.ArgumentList.Arguments.Select(x => Expr(x.Expression))) + "]",
        MemberAccessExpressionSyntax a => model.GetSymbolInfo(a).Symbol is IMethodSymbol { IsStatic: true } or IFieldSymbol { IsStatic: true } ? Name(model.GetSymbolInfo(a).Symbol!) : Expr(a.Expression) + a.OperatorToken.Text + a.Name,
        InvocationExpressionSyntax i => Expr(i.Expression) + "(" + string.Join(",", i.ArgumentList.Arguments.Select(a => a.RefKindKeyword.IsKind(SyntaxKind.None) ? Expr(a.Expression) : throw Error(a))) + ")",
        _ => throw Error(e)
    };
    static string Decl(VariableDeclarationSyntax d) => Type(d.Type) + " " + string.Join(",", d.Variables.Select(v => v.Identifier.Text + (v.Initializer is null ? "" : " = " + Expr(v.Initializer.Value))));
    static string Stmt(StatementSyntax s) => s switch
    {
        BlockSyntax b => "{\n" + string.Join("\n", b.Statements.Select(Stmt)) + "\n}",
        LocalDeclarationStatementSyntax d => Decl(d.Declaration) + ";",
        ExpressionStatementSyntax e => Expr(e.Expression) + ";",
        ReturnStatementSyntax r => "return" + (r.Expression is null ? "" : " " + Expr(r.Expression)) + ";",
        IfStatementSyntax i => "if (" + Expr(i.Condition) + ") " + Stmt(i.Statement) + (i.Else is null ? "" : " else " + Stmt(i.Else.Statement)),
        WhileStatementSyntax w => "while (" + Expr(w.Condition) + ") " + Stmt(w.Statement),
        ForStatementSyntax f => "for (" + (f.Declaration is null ? string.Join(",", f.Initializers.Select(Expr)) : Decl(f.Declaration)) + ";" + (f.Condition is null ? "" : Expr(f.Condition)) + ";" + string.Join(",", f.Incrementors.Select(Expr)) + ") " + Stmt(f.Statement),
        BreakStatementSyntax => "break;",
        ContinueStatementSyntax => "continue;",
        EmptyStatementSyntax => ";",
        CheckedStatementSyntax c => Stmt(c.Block),
        SwitchStatementSyntax sw => "switch (" + Expr(sw.Expression) + ") {\n" + string.Join("\n", sw.Sections.Select(sec => string.Join("\n", sec.Labels.Select(l => l is CaseSwitchLabelSyntax c ? "case " + Expr(c.Value) + ":" : l is DefaultSwitchLabelSyntax ? "default:" : throw Error(l))) + "\n" + string.Join("\n", sec.Statements.Select(Stmt)))) + "\n}",
        _ => throw Error(s)
    };
    static string Signature(MethodDeclarationSyntax m) => Type(m.ReturnType) + " " + Name(model.GetDeclaredSymbol(m)!) + "(" + (m.ParameterList.Parameters.Count == 0 ? "void" : string.Join(",", m.ParameterList.Parameters.Select(p => Type(p.Type!) + " " + p.Identifier.Text))) + ")";
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("CsToC OUTPUT CORE.cs HOST.cs [CORE2.cs ...]");
            var trees = args.Skip(1).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: Path.GetFullPath(path))).ToArray();
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("Portable", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException(string.Join('\n', errors.Select(e => e.ToString())));
            var result = new StringBuilder("/* Generated from the portable C# core; do not edit. */\n#include \"host.h\"\n");
            // HOST.cs participates in semantic checking, but is the platform boundary, not transpiled.
            var core = trees.Where((t, i) => i != 1).ToArray();
            foreach (var tree in core)
            {
                model = compilation.GetSemanticModel(tree);
                foreach (var st in tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>())
                {
                    result.AppendLine("typedef struct " + st.Identifier + " " + st.Identifier + ";");
                    result.AppendLine("struct " + st.Identifier + " {");
                    foreach (var f in st.Members.OfType<FieldDeclarationSyntax>()) result.AppendLine(Decl(f.Declaration) + ";");
                    result.AppendLine("};");
                }
            }
            foreach (var tree in core)
            {
                model = compilation.GetSemanticModel(tree);
                foreach (var c in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    foreach (var f in c.Members.OfType<FieldDeclarationSyntax>())
                        foreach (var v in f.Declaration.Variables)
                            result.AppendLine("static " + Type(f.Declaration.Type) + " " + Name(model.GetDeclaredSymbol(v)!) + (v.Initializer is null ? "" : " = " + Expr(v.Initializer.Value)) + ";");
                    foreach (var m in c.Members.OfType<MethodDeclarationSyntax>()) result.AppendLine(Signature(m) + ";");
                }
            }
            foreach (var tree in core)
            {
                model = compilation.GetSemanticModel(tree);
                foreach (var m in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    result.AppendLine($"#line {m.GetLocation().GetLineSpan().StartLinePosition.Line + 1} \"{tree.FilePath.Replace('\\', '/')}\"");
                    result.AppendLine(Signature(m) + (m.Body is null ? " { return " + Expr(m.ExpressionBody!.Expression) + "; }" : Stmt(m.Body)));
                }
            }
            result.AppendLine("\n#include \"entry.h\"");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
            File.WriteAllText(args[0], result.ToString());
            Console.WriteLine("Generated C from type-checked C#: " + args[0]); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
