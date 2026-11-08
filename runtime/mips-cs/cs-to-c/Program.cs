using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

// Deliberately small, fail-closed source backend for the portable safe C# core.
// Roslyn still type-checks the same source as ordinary C#. Unsupported syntax is an error.
static class Program
{
    static SemanticModel model = null!;
    static int temporary;
    static string Temp() => "pvm_tmp_" + temporary++;
    static string Name(ISymbol s) => s.ContainingType.Name + "_" + s.Name;
    static string Type(TypeSyntax t) => t switch
    {
        ArrayTypeSyntax a when a.RankSpecifiers.Count == 1 && a.RankSpecifiers[0].Rank == 1 && a.ElementType is not ArrayTypeSyntax && model.GetTypeInfo(a.ElementType).Type?.IsValueType == true => Type(a.ElementType) + "*",
        RefTypeSyntax r => Type(r.Type) + "*",
        PredefinedTypeSyntax p => p.Keyword.Text switch
        {
            "int" => "int32_t",
            "uint" => "uint32_t",
            "long" => "int64_t",
            "ulong" => "uint64_t",
            "byte" => "uint8_t",
            "sbyte" => "int8_t",
            "short" => "int16_t",
            "ushort" or "char" => "uint16_t",
            "bool" => "bool",
            "string" => "const char*",
            "void" => "void",
            _ => throw Error(t)
        },
        IdentifierNameSyntax i => i.Identifier.Text switch
        {
            "nint" => "intptr_t",
            "nuint" => "uintptr_t",
            _ => i.Identifier.Text
        },
        _ => throw Error(t)
    };
    static Exception Error(SyntaxNode n) => new NotSupportedException($"{n.SyntaxTree.FilePath}:{n.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: unsupported {n.Kind()}: {n}");
    static string Id(IdentifierNameSyntax i)
    {
        var s = model.GetSymbolInfo(i).Symbol;
        return s is IFieldSymbol { IsStatic: true } or IMethodSymbol { IsStatic: true } ? Name(s) :
            s is ILocalSymbol { RefKind: not RefKind.None } or IParameterSymbol { RefKind: not RefKind.None } ? "(*" + i.Identifier.Text + ")" : i.Identifier.Text;
    }

    static string Expr(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax i => Id(i),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.DefaultLiteralExpression) => "(" + model.GetTypeInfo(l).ConvertedType!.Name + "){0}",
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.NumericLiteralExpression) => NumericLiteral(l),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.CharacterLiteralExpression) => ((int)(char)l.Token.Value!).ToString(),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => StringLiteral(l),
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.NullLiteralExpression) => "((void*)0)",
        LiteralExpressionSyntax l => l.Token.Text,
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression) => "(" + Expr(b.Left) + " " + b.OperatorToken.Text + " " + Expr(b.Right) + ")",
        BinaryExpressionSyntax b => Binary(b),
        AssignmentExpressionSyntax a => Assignment(a),
        PrefixUnaryExpressionSyntax p => "(" + p.OperatorToken.Text + Expr(p.Operand) + ")",
        PostfixUnaryExpressionSyntax p when p.IsKind(SyntaxKind.SuppressNullableWarningExpression) => Expr(p.Operand),
        PostfixUnaryExpressionSyntax p => "(" + Expr(p.Operand) + p.OperatorToken.Text + ")",
        ParenthesizedExpressionSyntax p => "(" + Expr(p.Expression) + ")",
        CastExpressionSyntax c => "((" + Type(c.Type) + ")" + Expr(c.Expression) + ")",
        CheckedExpressionSyntax c when c.Keyword.IsKind(SyntaxKind.UncheckedKeyword) => Expr(c.Expression),
        DefaultExpressionSyntax d => "(" + Type(d.Type) + "){0}",
        SizeOfExpressionSyntax s => "sizeof(" + Type(s.Type) + ")",
        ConditionalExpressionSyntax c => "(" + Expr(c.Condition) + " ? " + Expr(c.WhenTrue) + " : " + Expr(c.WhenFalse) + ")",
        ElementAccessExpressionSyntax a when model.GetTypeInfo(a.Expression).Type?.SpecialType == SpecialType.System_String && a.ArgumentList.Arguments.Count == 1 =>
            StringElement(a),
        ElementAccessExpressionSyntax a when model.GetTypeInfo(a.Expression).Type is IArrayTypeSymbol && a.ArgumentList.Arguments.Count == 1 =>
            ArrayElement(a),
        ArrayCreationExpressionSyntax a when a.Initializer is null && a.Type.RankSpecifiers.Count == 1 && a.Type.RankSpecifiers[0].Sizes.Count == 1 =>
            "(" + Type(a.Type) + ")pvm_array(" + Expr((ExpressionSyntax)a.Type.RankSpecifiers[0].Sizes[0]) + ", sizeof(" + Type(a.Type.ElementType) + "))",
        RefExpressionSyntax r => "(&" + Expr(r.Expression) + ")",
        MemberAccessExpressionSyntax a when a.Name.Identifier.Text == "Length" && model.GetTypeInfo(a.Expression).Type?.SpecialType == SpecialType.System_String => "Host_Length(" + Expr(a.Expression) + ")",
        MemberAccessExpressionSyntax a when a.Name.Identifier.Text == "Length" && model.GetTypeInfo(a.Expression).Type is IArrayTypeSymbol => "pvm_length(" + Expr(a.Expression) + ")",
        MemberAccessExpressionSyntax a => model.GetSymbolInfo(a).Symbol is IMethodSymbol { IsStatic: true } or IFieldSymbol { IsStatic: true } ? Name(model.GetSymbolInfo(a).Symbol!) : Expr(a.Expression) + a.OperatorToken.Text + a.Name,
        InvocationExpressionSyntax i => Invocation(i),
        _ => throw Error(e)
    };
    // GNU statement expressions sequence C# operands left-to-right. The final
    // dereference remains an lvalue so assignment and ref both check once.
    static string StringElement(ElementAccessExpressionSyntax a)
    {
        string value = Temp(), index = Temp();
        return "({ __auto_type " + value + " = " + Expr(a.Expression) + "; __auto_type " + index + " = " + Expr(a.ArgumentList.Arguments[0].Expression) + "; pvm_string_at(" + value + ", " + index + "); })";
    }
    static string ArrayElement(ElementAccessExpressionSyntax a)
    {
        string array = Temp(), index = Temp();
        string element = CType(model.GetTypeInfo(a).Type!);
        return "(*({ __auto_type " + array + " = " + Expr(a.Expression) + "; __auto_type " + index + " = " +
            Expr(a.ArgumentList.Arguments[0].Expression) + "; (" + element + "*)pvm_at(" + array + ", " + index + ", sizeof(" + element + ")); }))";
    }
    static string StringLiteral(LiteralExpressionSyntax literal)
    {
        string value = (string)literal.Token.Value!;
        if (value.Any(c => c == 0 || c > 127)) { throw Error(literal); }
        // Fixed-width octal avoids C/C# differences in escapes and verbatim strings.
        return "\"" + string.Concat(value.Select(c => "\\" + Convert.ToString(c, 8)!.PadLeft(3, '0'))) + "\"";
    }
    static string NumericLiteral(LiteralExpressionSyntax literal)
    {
        string token = literal.Token.Text.Replace("_", "");
        var type = model.GetTypeInfo(literal).Type?.SpecialType;
        if (type == SpecialType.System_Int64 || type == SpecialType.System_UInt64)
        {
            token = token.TrimEnd('u', 'U', 'l', 'L') + (type == SpecialType.System_UInt64 ? "ULL" : "LL");
        }
        return token;
    }
    static string Promoted(ExpressionSyntax expression)
    {
        var type = model.GetTypeInfo(expression).ConvertedType;
        return type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
            ? "((" + CType(type) + ")(" + Expr(expression) + "))" : Expr(expression);
    }
    static string Binary(BinaryExpressionSyntax b)
    {
        string left = Temp(), right = Temp();
        return "({ __auto_type " + left + " = " + Promoted(b.Left) + "; __auto_type " + right + " = " + Promoted(b.Right) + "; " + left + " " + b.OperatorToken.Text + " " + right + "; })";
    }
    static string Assignment(AssignmentExpressionSyntax a)
    {
        string target = Temp(), value = Temp();
        if (a.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return "({ __auto_type " + target + " = &(" + Expr(a.Left) + "); __auto_type " + value + " = " + Expr(a.Right) + "; *" + target + " = " + value + "; })";
        }
        string op = a.OperatorToken.Text;
        if (!op.EndsWith('=') || op == "??=") { throw Error(a); }
        string prior = Temp();
        return "({ __auto_type " + target + " = &(" + Expr(a.Left) + "); __auto_type " + prior + " = *" + target + "; __auto_type " + value + " = " + Expr(a.Right) + "; *" + target + " = " + prior + " " + op[..^1] + " " + value + "; })";
    }
    static string Invocation(InvocationExpressionSyntax i)
    {
        if (model.GetSymbolInfo(i).Symbol is not IMethodSymbol { IsStatic: true }) { throw Error(i); }
        var prelude = new StringBuilder();
        var names = new List<string>();
        foreach (var argument in i.ArgumentList.Arguments)
        {
            if (argument.NameColon is not null) { throw Error(argument); }
            string name = Temp();
            string expression = argument.RefKindKeyword.IsKind(SyntaxKind.None) ? Expr(argument.Expression) :
                argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ? "(&" + Expr(argument.Expression) + ")" : throw Error(argument);
            prelude.Append("__auto_type ").Append(name).Append(" = ").Append(expression).Append("; ");
            names.Add(name);
        }
        return "({ " + prelude + Expr(i.Expression) + "(" + string.Join(", ", names) + "); })";
    }
    static string CType(ITypeSymbol t) => t switch
    {
        IArrayTypeSymbol a => CType(a.ElementType) + "*",
        _ => t.SpecialType switch
        {
            SpecialType.System_Int32 => "int32_t", SpecialType.System_UInt32 => "uint32_t",
            SpecialType.System_Int64 => "int64_t", SpecialType.System_UInt64 => "uint64_t",
            SpecialType.System_Byte => "uint8_t", SpecialType.System_SByte => "int8_t",
            SpecialType.System_Int16 => "int16_t", SpecialType.System_UInt16 or SpecialType.System_Char => "uint16_t",
            SpecialType.System_Boolean => "bool", SpecialType.System_String => "const char*",
            _ when t.TypeKind == TypeKind.Struct => t.Name,
            _ => throw new NotSupportedException("unsupported array element type " + t)
        }
    };
    static string Decl(VariableDeclarationSyntax d)
    {
        // A C pointer declarator applies to only one variable, unlike C# array types.
        if (d.Variables.Count > 1 && d.Type is ArrayTypeSyntax or RefTypeSyntax) { throw Error(d); }
        if (d.Type.IsVar) { throw Error(d); }
        return Type(d.Type) + " " + string.Join(",", d.Variables.Select(v => v.Identifier.Text + (v.Initializer is null ? "" : " = " + Expr(v.Initializer.Value))));
    }
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
        CheckedStatementSyntax c when c.Keyword.IsKind(SyntaxKind.UncheckedKeyword) => Stmt(c.Block),
        SwitchStatementSyntax sw => "switch (" + Expr(sw.Expression) + ") {\n" + string.Join("\n", sw.Sections.Select(sec => string.Join("\n", sec.Labels.Select(l => l is CaseSwitchLabelSyntax c ? "case " + Expr(c.Value) + ":" : l is DefaultSwitchLabelSyntax ? "default:" : throw Error(l))) + "\n" + string.Join("\n", sec.Statements.Select(Stmt)))) + "\n}",
        _ => throw Error(s)
    };
    static string Signature(MethodDeclarationSyntax m) => Type(m.ReturnType) + " " + Name(model.GetDeclaredSymbol(m)!) + "(" + (m.ParameterList.Parameters.Count == 0 ? "void" : string.Join(",", m.ParameterList.Parameters.Select(p => Type(p.Type!) + (p.Modifiers.Any(SyntaxKind.RefKeyword) ? "*" : "") + " " + p.Identifier.Text))) + ")";
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3)
            {
                throw new ArgumentException("CsToC OUTPUT CORE.cs HOST.cs [CORE2.cs ...]");
            }

            var trees = args.Skip(1).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: Path.GetFullPath(path))).ToArray();
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("Portable", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(string.Join('\n', errors.Select(e => e.ToString())));
            }

            var result = new StringBuilder("/* Generated from the portable C# core; do not edit. */\n#include \"host.h\"\n");
            // HOST.cs participates in semantic checking, but is the platform boundary, not transpiled.
            var core = trees.Where((t, i) => i != 1).ToArray();
            foreach (var tree in core)
            {
                model = compilation.GetSemanticModel(tree);
                foreach (var st in tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>())
                {
                    if (st.Members.Any(m => m is not FieldDeclarationSyntax)) { throw Error(st); }
                    result.AppendLine("typedef struct " + st.Identifier + " " + st.Identifier + ";");
                    result.AppendLine("struct " + st.Identifier + " {");
                    foreach (var f in st.Members.OfType<FieldDeclarationSyntax>())
                    {
                        result.AppendLine(Decl(f.Declaration) + ";");
                    }

                    result.AppendLine("};");
                }
            }

            foreach (var tree in core)
            {
                model = compilation.GetSemanticModel(tree);
                foreach (var c in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!c.Modifiers.Any(SyntaxKind.StaticKeyword) || c.Members.Any(m => m is not FieldDeclarationSyntax and not MethodDeclarationSyntax)) { throw Error(c); }
                    if (c.Members.OfType<MethodDeclarationSyntax>().Any(m => !m.Modifiers.Any(SyntaxKind.StaticKeyword))) { throw Error(c); }
                    foreach (var f in c.Members.OfType<FieldDeclarationSyntax>())
                    {
                        foreach (var v in f.Declaration.Variables)
                        {
                            result.AppendLine("static " + Type(f.Declaration.Type) + " " + Name(model.GetDeclaredSymbol(v)!) + (v.Initializer is null ? "" : " = " + Expr(v.Initializer.Value)) + ";");
                        }
                    }

                    foreach (var m in c.Members.OfType<MethodDeclarationSyntax>())
                    {
                        result.AppendLine(Signature(m) + ";");
                    }
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
            Console.WriteLine("Generated C from type-checked C#: " + args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
