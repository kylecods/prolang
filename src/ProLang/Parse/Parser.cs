using ProLang.Syntax;

namespace ProLang.Parse;

internal sealed class Parser
{
    private readonly SyntaxToken[] _tokens;

    private List<string> _diagnostics = new();

    private int _position;

    public Parser(string text)
    {
        var tokens = new List<SyntaxToken>();

        var lexer = new Lexer(text);

        SyntaxToken token;
        do
        {
            token = lexer.NextToken();

            if (token.Kind != SyntaxKind.WhitespaceToken && token.Kind != SyntaxKind.BadToken)
            {
                tokens.Add(token);
            }
        } while (token.Kind != SyntaxKind.EofToken);

        _tokens = tokens.ToArray();
        
        _diagnostics.AddRange(lexer.Diagnostics);

    }

    public IEnumerable<string> Diagnostics => _diagnostics;

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
        
        _diagnostics.Add($"ERROR: Unexpected token <{Current.Kind}>, expected <{kind}>");

        return new SyntaxToken(kind, Current.Position, null!, null!);
    }

    private ExpressionSyntax ParseExpression()
    {
        return ParseTerm();
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
            node = ParseTerm();
        }
        
        var eofToken = Match(SyntaxKind.EofToken);

        return new SyntaxTree(_diagnostics, node, eofToken);
    }

    private ExpressionSyntax ParseTerm()
    {
        var left = ParseFactor();

        while (Current.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken)
        {
            var operatorToken = NextToken();

            var right = ParsePrimaryExpression();

            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseFactor()
    {
        var left = ParsePrimaryExpression();

        while (Current.Kind is SyntaxKind.StarToken or SyntaxKind.SlashToken)
        {
            var operatorToken = NextToken();
            var right = ParsePrimaryExpression();

            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParsePrimaryExpression()
    {
        if (Current.Kind == SyntaxKind.LeftParenthesisToken)
        {
            var left = NextToken();
            var expression = ParseExpression();
            var right = Match(SyntaxKind.RightParenthesisToken);

            return new ParenthesisExpressionSyntax(left, expression, right);
        }
        
        var numberToken = Match(SyntaxKind.NumberToken);

        return new NumberExpressionSyntax(numberToken);
    }

    private StatementSyntax VariableStatement()
    {
        var letKeyword = Match(SyntaxKind.LetKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var equalsToken = Match(SyntaxKind.EqualsToken);
        var expression = ParseTerm();

        return new VariableStatementSyntax(letKeyword,identifier,equalsToken,expression);
    }
}