using System.CodeDom.Compiler;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Intermediate;

internal static class BoundNodePrinter
{
    public static void WriteTo(this BoundNode node, TextWriter writer)
    {
        if (writer is IndentedTextWriter iw)
        {
            WriteTo(node,iw);
        }
        else
        {
            WriteTo(node, new IndentedTextWriter(writer));
        }
    }

    public static void WriteTo(this BoundNode node, IndentedTextWriter writer)
    {
        switch (node.Kind)
        {
            case BoundNodeKind.BlockStatement:
                WriteBlockStatement((BoundBlockStatement)node, writer);
                break;
            case BoundNodeKind.VariableDeclaration:
                WriteVariableDeclaration((BoundVariableDeclaration)node, writer);
                break;
            case BoundNodeKind.IfStatement:
                WriteIfStatement((BoundIfStatement)node, writer);
                break;
            case BoundNodeKind.WhileStatement:
                WriteWhileStatement((BoundWhileStatement)node, writer);
                break;
            case BoundNodeKind.ForStatement:
                WriteForStatement((BoundForStatement)node, writer);
                break;
            case BoundNodeKind.LabelStatement:
                WriteLabelStatement((BoundLabelStatement)node, writer);
                break;
            case BoundNodeKind.GotoStatement:
                WriteGotoStatement((BoundGotoStatement)node, writer);
                break;
            case BoundNodeKind.ConditionalGotoStatement:
                WriteConditionalGotoStatement((BoundConditionalGotoStatement)node, writer);
                break;
            case BoundNodeKind.ReturnStatement:
                WriteReturnStatement((BoundReturnStatement)node, writer);
                break;
            case BoundNodeKind.ExpressionStatement:
                WriteExpressionStatement((BoundExpressionStatement)node, writer);
                break;
            case BoundNodeKind.BoundErrorExpression:
                WriteErrorExpression((BoundErrorExpression)node, writer);
                break;
            case BoundNodeKind.BoundLiteralExpression:
                WriteLiteralExpression((BoundLiteralExpression)node, writer);
                break;
            case BoundNodeKind.BoundVariableExpression:
                WriteVariableExpression((BoundVariableExpression)node, writer);
                break;
            case BoundNodeKind.BoundUnaryExpression:
                WriteUnaryExpression((BoundUnaryExpression)node, writer);
                break;
            case BoundNodeKind.BoundBinaryExpression:
                WriteBinaryExpression((BoundBinaryExpression)node, writer);
                break;
            case BoundNodeKind.BoundAssignmentExpression:
                WriteAssignmentExpression((BoundAssignmentExpression)node, writer);
                break;

            case BoundNodeKind.BoundCallExpression:
                WriteCallExpression((BoundCallExpression)node, writer);
                break;
            case BoundNodeKind.BoundConversionExpression:
                WriteConversionExpression((BoundConversionExpression)node, writer);
                break;
            default:
                throw new Exception($"Unexpected node {node.Kind}");
            break;
        }
    }
    
    private static void WriteNestedStatement(this IndentedTextWriter writer, BoundStatement node)
    {
        var needsIndentation = !(node is BoundBlockStatement);

        if (needsIndentation)
        {
            writer.Indent++;
        }
        
        node.WriteTo(writer);

        if (needsIndentation)
        {
            writer.Indent--;
        }
    }

    private static void WriteNestedExpression(this IndentedTextWriter writer, int parentPrecedence,
        BoundExpression expression)
    {
        if (expression is BoundUnaryExpression unary)
        {
            writer.WriteNestedExpression(parentPrecedence,SyntaxFacts.GetUnaryOperatorPrecedence(unary.Op.SyntaxKind),unary);
        }else if (expression is BoundBinaryExpression binary)
        {
            writer.WriteNestedExpression(parentPrecedence,SyntaxFacts.GetBinaryOperatorPrecedence(binary.Op.SyntaxKind),binary);
            
        }
        else
        {
            expression.WriteTo(writer);
        }
    }

    private static void WriteNestedExpression(this IndentedTextWriter writer, int parentPrecedence,
        int currentPrecedence, BoundExpression expression)
    {
        var needsParenthesis = parentPrecedence >= currentPrecedence;

        if (needsParenthesis)
        {
            writer.WritePunctuation("(");
        }
        
        expression.WriteTo(writer);

        if (needsParenthesis)
        {
            writer.WritePunctuation(")");
        }
    }
    
    private static void WriteBlockStatement(BoundBlockStatement node, IndentedTextWriter writer)
    {
        writer.WritePunctuation("{");
        writer.WriteLine();
        writer.Indent++;

        foreach (var s in node.Statements)
        {
            s.WriteTo(writer);
        }

        writer.Indent--;
        writer.WritePunctuation("}");
        writer.WriteLine();
    }

    private static void WriteVariableDeclaration(BoundVariableDeclaration node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("let");
        writer.WriteIdentifier(node.Variable.Name);
        writer.WritePunctuation("=");
        node.Initializer.WriteTo(writer);
        writer.WriteLine();
    }

    private static void WriteIfStatement(BoundIfStatement node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("if");
        writer.WritePunctuation("(");
        node.Condition.WriteTo(writer);
        writer.WritePunctuation(")");
        writer.WriteLine();
        writer.WritePunctuation("{");
        writer.Indent++;
        writer.WriteNestedStatement(node.Body);
        writer.Indent--;
        writer.WritePunctuation("}");
        writer.WriteLine();

        if (node.ElseStatement != null)
        {
            writer.WriteKeyword("else");
            writer.WriteLine();
            writer.WritePunctuation("{");
            writer.Indent++;
            writer.WriteNestedStatement(node.ElseStatement);
            writer.Indent--;
            writer.WritePunctuation("}");
            writer.WriteLine();
        }
    }
    
    private static void WriteWhileStatement(BoundWhileStatement node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("while");
        writer.WritePunctuation("(");
        node.Condition.WriteTo(writer);
        writer.WritePunctuation(")");
        writer.WriteLine();
        writer.WritePunctuation("{");
        writer.Indent++;
        writer.WriteNestedStatement(node.Body);
        writer.Indent--;
        writer.WritePunctuation("}");
        writer.WriteLine();
    }
    
    private static void WriteForStatement(BoundForStatement node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("for");
        writer.WritePunctuation("(");
        writer.WriteIdentifier(node.Variable.Name);
        writer.WritePunctuation("=");
        node.LowerBound.WriteTo(writer);
        writer.WriteKeyword("to");
        node.UpperBound.WriteTo(writer);
        writer.WritePunctuation(")");
        writer.WriteLine();
        writer.WriteNestedStatement(node.Body);
        
    }
    
    private static void WriteLabelStatement(BoundLabelStatement node, IndentedTextWriter writer)
    {
        var unIndent = writer.Indent > 0;
        if (unIndent)
        {
            writer.Indent--;
        }
        writer.WritePunctuation(node.BoundLabel.Name);
        writer.WritePunctuation(":");
        writer.WriteLine();

        if (unIndent)
        {
            writer.Indent++;
        }
    }

    private static void WriteGotoStatement(BoundGotoStatement node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("goto ");
        writer.WriteIdentifier(node.BoundLabel.Name);
        writer.WriteLine();
    }
    
    private static void WriteConditionalGotoStatement(BoundConditionalGotoStatement node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("goto ");
        writer.WriteIdentifier(node.BoundLabel.Name);
        writer.WriteKeyword(node.JumpIfTrue ? " if ": " unless ");
        node.Condition.WriteTo(writer);
        writer.WriteLine();
    }
    
    private static void WriteExpressionStatement(BoundExpressionStatement node, IndentedTextWriter writer)
    {
        node.Expression.WriteTo(writer);
        writer.WriteLine();
    }
    
    private static void WriteErrorExpression(BoundErrorExpression node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("?");
    }
    
    private static void WriteLiteralExpression(BoundLiteralExpression node, IndentedTextWriter writer)
    {
        var value = node.Value.ToString();

        if (node.Type == TypeSymbol.Bool)
        {
            writer.WriteKeyword(value!);
        }
        else if(node.Type == TypeSymbol.Int)
        {
            writer.WriteNumber(value!);
        }else if (node.Type == TypeSymbol.String)
        {
            value = "\"" + value!.Replace("\"", "\"\"") + "\"";
            writer.WriteString(value);
        }
        else
        {
            throw new Exception($"Unexpected type {node.Type}");
        }
    }
    
    private static void WriteVariableExpression(BoundVariableExpression node, IndentedTextWriter writer)
    {
        writer.WriteIdentifier(node.Variable.Name);
    }

    private static void WriteAssignmentExpression(BoundAssignmentExpression node, IndentedTextWriter writer)
    {
        writer.WriteIdentifier(node.Variable.Name);
        writer.WritePunctuation("=");
        node.Expression.WriteTo(writer);
    }
    
    private static void WriteUnaryExpression(BoundUnaryExpression node, IndentedTextWriter writer)
    {
        var op = SyntaxFacts.GetText(node.Op.SyntaxKind);
        var precedence = node.Op.SyntaxKind.GetUnaryOperatorPrecedence();
        
        writer.WritePunctuation(op);
        writer.WriteNestedExpression(precedence,node.Operand);
    }
    
    private static void WriteBinaryExpression(BoundBinaryExpression node, IndentedTextWriter writer)
    {
        var op = SyntaxFacts.GetText(node.Op.SyntaxKind);
        var precedence = node.Op.SyntaxKind.GetBinaryOperatorPrecedence();
        
        writer.WriteNestedExpression(precedence,node.Left);
        
        writer.Write(" ");
        writer.WritePunctuation(op);
        writer.Write(" ");
        writer.WriteNestedExpression(precedence,node.Right);
    }

    private static void WriteCallExpression(BoundCallExpression node, IndentedTextWriter writer)
    {
        writer.WriteIdentifier(node.Function.Name);
        writer.WritePunctuation("(");

        var isFirst = true;

        foreach (var argument in node.Arguments)
        {
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                writer.WritePunctuation(", ");
            }
            
            argument.WriteTo(writer);
        }
        
        writer.WritePunctuation(")");
    }
    
    private static void WriteConversionExpression(BoundConversionExpression node, IndentedTextWriter writer)
    {
        writer.WriteIdentifier(node.Type.Name);
        
        writer.WritePunctuation("(");
        node.Expression.WriteTo(writer);
        writer.WritePunctuation(")");
    }

    private static void WriteReturnStatement(BoundReturnStatement node, IndentedTextWriter writer)
    {
        writer.WriteKeyword("return");

        if (node.Expression != null)
        {
            writer.Write(" ");
            node.Expression.WriteTo(writer);
        }
        writer.WriteLine();
    }

}