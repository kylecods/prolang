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
    
    public GlobalDeclarationSyntax ParseGlobalDeclaration()
    {
        var statement = ParseDeclarations();
        
        var eofToken = Match(SyntaxKind.EofToken);


        return new GlobalDeclarationSyntax(statement,eofToken);
    }

    private ImmutableArray<DeclarationSyntax> ParseDeclarations()
    {
        var statements = ImmutableArray.CreateBuilder<DeclarationSyntax>();

        while (Current.Kind != SyntaxKind.EofToken)
        {
            var startToken = Current;

            var statement = ParseDeclaration();
            
            statements.Add(statement);

            if (Current == startToken)
            {
                NextToken();
            }
        }

        return statements.ToImmutable();
    }

    private DeclarationSyntax ParseDeclaration()
    {
        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            return ParseHtmlDeclaration();
        }

        return ParseGlobalStatement();
    }

    private GlobalStatementSyntax ParseGlobalStatement()
    {
        var statement = ParseAnyStatement();

        return new GlobalStatementSyntax(statement);
    }

    private StatementSyntax ParseAnyStatement()
    {
        var statement = Current.Kind switch
        {
            SyntaxKind.LetKeyword => ParseVariableStatement(),
            SyntaxKind.LessThanToken => ParseHtmlStatement(),
            SyntaxKind.LeftCurlyToken => ParseBlockStatement(),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            _ => ParseExpressionStatement()
        };
        return statement;
    }

    private StatementSyntax ParseIfStatement()
    {
        var ifKeyword = Match(SyntaxKind.IfKeyword);
        var openToken = Match(SyntaxKind.LeftParenthesisToken);
        var condition = ParseExpression();
        var closeToken = Match(SyntaxKind.RightParenthesisToken);
        var blockStatement = ParseBlockStatement();
        var elIfClause = ParseElIfClause();
        var elseClause = ParseElseClause();

        return new IfStatementSyntax(ifKeyword, openToken, condition, closeToken, blockStatement, elIfClause, elseClause);
    }

    private ElseClauseSyntax? ParseElseClause()
    {
        if (Current.Kind != SyntaxKind.ElseKeyword)
        {
            return null;
        }

        var elseKeyword = Match(SyntaxKind.ElseKeyword);

        var blockStatement = ParseBlockStatement();

        return new ElseClauseSyntax(elseKeyword, blockStatement);
    }

    private ElseIfClauseSyntax? ParseElIfClause()
    {
        if (Current.Kind != SyntaxKind.ElIfKeyword)
        {
            return null;
        }

        var elseIfKeyword = Match(SyntaxKind.ElIfKeyword);

        var condition = ParseExpression();

        var blockStatement = ParseBlockStatement();

        return new ElseIfClauseSyntax(elseIfKeyword, condition, blockStatement);
    }

    private StatementSyntax ParseBlockStatement()
    {
        var openCurlyToken = Match(SyntaxKind.LeftCurlyToken);

        var proLangStatements = ImmutableArray.CreateBuilder<StatementSyntax>();

        while (Current.Kind != SyntaxKind.EofToken && Current.Kind != SyntaxKind.RightCurlyToken)
        {
            var startToken = Current;
            
            var proLangStatement = ParseProLangStatement();
            
            proLangStatements.Add(proLangStatement);

            if (Current == startToken)
            {
                NextToken();
            }
            
        }

        var closeCurlyToken = Match(SyntaxKind.RightCurlyToken);

        return new BlockStatementSyntax(openCurlyToken,proLangStatements.ToImmutable(),closeCurlyToken);
    }

    private StatementSyntax ParseExpressionStatement()
    {
        var expression = ParseExpression();

        return new ExpressionStatementSyntax(expression);
    }
    
    private HtmlDeclarationSyntax ParseHtmlDeclaration()
    {
        var nodes = ImmutableArray.CreateBuilder<HtmlStatementSyntax>();

        while (Current.Kind != SyntaxKind.EofToken)
        {
            var startToken = Current;
            
            var statement = ParseHtmlStatement();
            
            nodes.Add(statement);
            
            if (Current == startToken)
            {
                NextToken();
            }
        }
        
        return new HtmlDeclarationSyntax( nodes.ToImmutable());
    }

    private HtmlStatementSyntax ParseHtmlStatement()
    {
        var htmlStartTag = ParseHtmlStartTag();
        
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();

        while (Current.Kind != SyntaxKind.EofToken && Current.Kind != SyntaxKind.OpenAngleForwardSlashToken)
        {
            var startToken = Current;
            
            var statement = ParseProLangHtmlCompatibleStatement();
            
            statements.Add(statement);
            
            if (Current == startToken)
            {
                NextToken();
            }
        }
        
        
        var htmlEndTag = ParseHtmlEndTag();

        return new HtmlStatementSyntax(htmlStartTag,statements.ToImmutable(), htmlEndTag);
    }

    private HtmlStartTagSyntax ParseHtmlStartTag()
    {
        var leftAngle = Match(SyntaxKind.LessThanToken);
        var htmlKeyword = Match(SyntaxKind.IdentifierToken);
        var rightAngle = Match(SyntaxKind.GreaterThanToken);
        
        return new HtmlStartTagSyntax(leftAngle,htmlKeyword,rightAngle);
    }

    private HtmlEndTagSyntax ParseHtmlEndTag()
    {
        var closeToken = Match(SyntaxKind.OpenAngleForwardSlashToken);
        var htmlKeyword = Match(SyntaxKind.IdentifierToken);
        var rightAngle = Match(SyntaxKind.GreaterThanToken);

        return new HtmlEndTagSyntax(closeToken,htmlKeyword, rightAngle);
    }

    private ExpressionSyntax ParseExpression()
    {
        return ParseAssignmentExpression();
    }

    private ExpressionSyntax ParseBinaryExpression(int parentPrecedence = 0)
    {
        ExpressionSyntax left;

        var unaryOperator = Current.Kind.GetUnaryOperatorPrecedence();

        if (unaryOperator != 0 && unaryOperator >= parentPrecedence)
        {
            var operatorToken = NextToken();

            var operand = ParseBinaryExpression(unaryOperator);

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
            var right = ParseBinaryExpression(precedence);

            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseAssignmentExpression()
    {
        if (Peek(0).Kind == SyntaxKind.IdentifierToken)
        {
            switch (Peek(1).Kind)
            {
                case SyntaxKind.PlusEqualsToken:
                case SyntaxKind.MinusEqualsToken:
                case SyntaxKind.StarEqualsToken:
                case SyntaxKind.SlashEqualsToken:
                case SyntaxKind.EqualsToken:
                    var identifierToken = NextToken();
                    var operatorToken = NextToken();
                    var expression = ParseAssignmentExpression();
                    return new AssignmentExpressionSyntax(identifierToken, operatorToken, expression);
                    
            }
        }

        return ParseBinaryExpression();
    }

    private ExpressionSyntax ParsePrimaryExpression()
    {
        switch (Current.Kind)
        {
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            {
                return ParseBooleanLiteral();
            }
            case SyntaxKind.LeftParenthesisToken:
            {
                return ParseParenthesizedExpression();
            }
            case SyntaxKind.NumberToken:
                return ParseNumberLiteral(); 
            case SyntaxKind.IdentifierToken:
                return ParseNameExpression();
            default:
                return ParseStringLiteral();
        }
    }

    private ExpressionSyntax ParseNameExpression()
    {
        var identifierToken = Match(SyntaxKind.IdentifierToken);

        return new NameExpressionSyntax(identifierToken);
    }

    private ExpressionSyntax ParseStringLiteral()
    {
        var numberToken = Match(SyntaxKind.StringToken);
        return new LiteralExpressionSyntax(numberToken);
    }

    private ExpressionSyntax ParseNumberLiteral()
    {
        var numberToken = Match(SyntaxKind.NumberToken);
        return new LiteralExpressionSyntax(numberToken);
    }

    private ExpressionSyntax ParseParenthesizedExpression()
    {
        var left = Match(SyntaxKind.LeftParenthesisToken);
        var expression = ParseExpression();
        var right = Match(SyntaxKind.RightParenthesisToken);

        return new ParenthesisExpressionSyntax(left, expression, right);
    }

    private ExpressionSyntax ParseBooleanLiteral()
    {
        var isTrue = Current.Kind == SyntaxKind.TrueKeyword;

        var keywordToken = isTrue ? Match(SyntaxKind.TrueKeyword) : Match(SyntaxKind.FalseKeyword);

        return new LiteralExpressionSyntax(keywordToken, isTrue);
    }

    private StatementSyntax ParseVariableStatement()
    {
        var letKeyword = Match(SyntaxKind.LetKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var equalsToken = Match(SyntaxKind.EqualsToken);
        var expression = ParseExpression();

        return new VariableStatementSyntax(letKeyword,identifier,equalsToken,expression);
    }

    private StatementSyntax ParseProLangBlockStatement()
    {
        var openCurlyToken = Match(SyntaxKind.DollarCurlyToken);

        var proLangStatements = ImmutableArray.CreateBuilder<StatementSyntax>();

        while (Current.Kind != SyntaxKind.EofToken && Current.Kind != SyntaxKind.RightCurlyToken)
        {
            var startToken = Current;
            //we want to only parse ProLang syntax here
            var proLangStatement = ParseProLangStatement();
            
            proLangStatements.Add(proLangStatement);

            if (Current == startToken)
            {
                NextToken();
            }
            
        }

        var closeCurlyToken = Match(SyntaxKind.RightCurlyToken);

        return new HtmlProLangBlockStatementSyntax(openCurlyToken, proLangStatements.ToImmutable(), closeCurlyToken);
    }

    private StatementSyntax ParseProLangStatement()
    {
        var statement = Current.Kind switch
        {
            SyntaxKind.LetKeyword => ParseVariableStatement(),
            SyntaxKind.LeftCurlyToken => ParseBlockStatement(),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            _ => ParseExpressionStatement()
        };
        return statement;
        
    }

    private StatementSyntax ParseForStatement()
    {
        var forKeyword = Match(SyntaxKind.ForKeyword);
        var openToken = Match(SyntaxKind.LeftParenthesisToken);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var equalToken = Match(SyntaxKind.EqualsToken);
        var lowerBound = ParseExpression();
        var toKeyword = Match(SyntaxKind.ToKeyword);
        var upBound = ParseExpression();
        var closeToken = Match(SyntaxKind.RightParenthesisToken);
        var body = ParseBlockStatement();

        return new ForStatementSyntax(forKeyword, openToken, identifier, equalToken, lowerBound, toKeyword, upBound,closeToken,
            body);
    }

    private StatementSyntax ParseWhileStatement()
    {
        var whileKeyword = Match(SyntaxKind.WhileKeyword);
        var openToken = Match(SyntaxKind.LeftParenthesisToken);
        var condition = ParseExpression();
        var closeToken = Match(SyntaxKind.RightParenthesisToken);

        var blockStatement = ParseBlockStatement();

        return new WhileStatementSyntax(whileKeyword, openToken, condition, closeToken, blockStatement);
    }

    private StatementSyntax ParseProLangHtmlCompatibleStatement()
    {
        var statement = Current.Kind switch
        {
            SyntaxKind.DollarCurlyToken => ParseProLangBlockStatement(),
            SyntaxKind.LessThanToken => ParseHtmlStatement(),
            _ => ParseExpressionStatement()
        };
        return statement;
    }
}