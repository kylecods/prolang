using System.Collections.Immutable;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Parse;

internal sealed class Parser
{
    private DiagnosticBag _diagnostics = new();
    
    private readonly SourceText _text;

    private readonly ImmutableArray<SyntaxToken> _tokens;

    private int _position;

    public Parser(SourceText text)
    {
        var tokens = new List<SyntaxToken>();

        var lexer = new Lexer(text);

        SyntaxToken token;
        do
        {
            token = lexer.Lex();

            if (token.Kind != SyntaxKind.WhitespaceToken && token.Kind != SyntaxKind.BadToken)
            {
                tokens.Add(token);
            }
        } while (token.Kind != SyntaxKind.EofToken);

        _tokens = tokens.ToImmutableArray();
        
        _text = text;
        
        _diagnostics.AddRange(lexer.Diagnostics);

    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private SyntaxToken Peek(int offset)
    {
        var index = _position + offset;

        if (index >= _tokens.Length)
        {
            return _tokens[_tokens.Length + 1];
        }

        return _tokens[index];
    }

    private SyntaxToken Current => Peek(0);

    private SyntaxToken NextToken()
    {
        var current = Current;
        _position++;

        return current;
    }

    private SyntaxToken Match(SyntaxKind kind)
    {
        if (Current.Kind == kind) return NextToken();
        
        _diagnostics.ReportUnexpectedToken(Current.Span,Current.Kind,kind);

        return new SyntaxToken(kind, Current.Position, null!, null!);
    }

    public SyntaxTree Parse()
    {
        SyntaxNode node;
        
        if (Current.Kind == SyntaxKind.LetKeyword)
        {
            node = VariableStatement();
        }
        else
        {
            node = ParseExpression();
        }
        
        var eofToken = Match(SyntaxKind.EofToken);

        return new SyntaxTree(_text,_diagnostics.ToImmutableArray(), node, eofToken);
    }

    private ExpressionSyntax ParseExpression(int parentPrecedence = 0)
    {
        
        ExpressionSyntax left;

        var unaryOperator = Current.Kind.GetUnaryOperatorPrecedence();

        if (unaryOperator != 0 && unaryOperator >= parentPrecedence)
        {
            var operatorToken = NextToken();

            var operand = ParseExpression(unaryOperator);

            left = new UnaryExpressionSyntax(operatorToken, operand);
        }
        else
        {
            left = ParsePrimaryExpression();
        }
        
        while (true)
        {
            var precedence = Current.Kind.GetBinaryOperatorPrecedence();
            if (precedence == 0 || precedence <= parentPrecedence)
                break;
            var operatorToken = NextToken();
            var right = ParseExpression(precedence);

            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParsePrimaryExpression()
    {
        switch (Current.Kind)
        {
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            {
                var keyword = NextToken();

                var value = keyword.Kind == SyntaxKind.FalseKeyword;

                return new LiteralExpressionSyntax(keyword, value);
            }
            case SyntaxKind.LeftParenthesisToken:
            {
                var left = NextToken();
                var expression = ParseExpression();
                var right = Match(SyntaxKind.RightParenthesisToken);

                return new ParenthesisExpressionSyntax(left, expression, right);
            }
            default:
                var numberToken = Match(SyntaxKind.NumberToken);
                return new LiteralExpressionSyntax(numberToken);
        }
    }

    private StatementSyntax VariableStatement()
    {
        var letKeyword = Match(SyntaxKind.LetKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var equalsToken = Match(SyntaxKind.EqualsToken);
        var expression = ParseExpression();

        return new VariableStatementSyntax(letKeyword,identifier,equalsToken,expression);
    }
}