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

    private readonly SyntaxTree _syntaxTree;

    public Parser(SyntaxTree syntaxTree)
    {
        var tokens = new List<SyntaxToken>();

        var lexer = new Lexer(syntaxTree);

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

        _syntaxTree = syntaxTree;
        
        _text = syntaxTree.Text;
        
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
        
        _diagnostics.ReportUnexpectedToken(Current.Location,Current.Kind,kind);

        return new SyntaxToken(_syntaxTree,kind, Current.Position, null!, null!);
    }
    
    public GlobalDeclarationSyntax ParseGlobalDeclaration()
    {
        var declarations = ParseDeclarations();
        
        var eofToken = Match(SyntaxKind.EofToken);
        
        return new GlobalDeclarationSyntax(_syntaxTree,declarations,eofToken);
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

        if (Current.Kind == SyntaxKind.FunctionKeyword)
        {
            return ParseFunctionDeclaration();
        }

        return ParseGlobalStatement();
    }

    private DeclarationSyntax ParseFunctionDeclaration()
    {
        var functionKeyword = Match(SyntaxKind.FunctionKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var openParenthesisToken = Match(SyntaxKind.LeftParenthesisToken);
        var parameters = ParseParameterList();

        var closeParenthesisToken = Match(SyntaxKind.RightParenthesisToken);

        var type = ParseOptionalTypeClause();
        var body = ParseBlockStatement();

        return new FunctionDeclarationSyntax(_syntaxTree,functionKeyword, identifier, openParenthesisToken, parameters,
            closeParenthesisToken, type, (BlockStatementSyntax)body);
    }
    
    private SeparatedSyntaxList<ParameterSyntax> ParseParameterList()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<SyntaxNode>();

        var parseNextParameter = true;

        while (parseNextParameter && Current.Kind != SyntaxKind.RightParenthesisToken && Current.Kind != SyntaxKind.EofToken)
        {
            var parameter = ParseParameter();
            nodesAndSeparators.Add(parameter);

            if (Current.Kind == SyntaxKind.CommaToken)
            {
                var comma = Match(SyntaxKind.CommaToken);
                nodesAndSeparators.Add(comma);
            }
            else
            {
                parseNextParameter = false;
            }
        }

        return new SeparatedSyntaxList<ParameterSyntax>(nodesAndSeparators.ToImmutable());
    }

    private ParameterSyntax ParseParameter()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        var type = ParseTypeClause();
        return new ParameterSyntax(_syntaxTree,identifier, type);
    }

    private TypeClauseSyntax? ParseOptionalTypeClause()
    {
        if (Current.Kind != SyntaxKind.ColonToken)
        {
            return null;
        }

        return ParseTypeClause();
    }
    
    private TypeClauseSyntax ParseTypeClause()
    {
        var colonToken = Match(SyntaxKind.ColonToken);
        var identifier = Match(SyntaxKind.IdentifierToken);

        return new TypeClauseSyntax(_syntaxTree,colonToken, identifier);
    }

    private GlobalStatementSyntax ParseGlobalStatement()
    {
        var statement = ParseProLangStatement();

        return new GlobalStatementSyntax(_syntaxTree,statement);
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
            SyntaxKind.ReturnKeyword => ParseReturnStatement(),
            _ => ParseExpressionStatement()
        };
        return statement;
    }

    private StatementSyntax ParseReturnStatement()
    {
        var returnKeyword = Match(SyntaxKind.ReturnKeyword);
        var keywordLine = _text.GetLineIndex(returnKeyword.Span.Start);
        var currentLine = _text.GetLineIndex(Current.Span.Start);

        var isEof = Current.Kind == SyntaxKind.EofToken;

        var sameLine = !isEof && keywordLine == currentLine;

        var expression = sameLine ? ParseExpression() : null;

        return new ReturnStatementSyntax(_syntaxTree,returnKeyword, expression);
    }

    private StatementSyntax ParseIfStatement()
    {
        var ifKeyword = Match(SyntaxKind.IfKeyword);
        var openToken = Match(SyntaxKind.LeftParenthesisToken);
        var condition = ParseExpression();
        var closeToken = Match(SyntaxKind.RightParenthesisToken);
        var statement = ParseProLangStatement();// later we ll support html too
        var elIfClause = ParseElIfClause();
        var elseClause = ParseElseClause();

        return new IfStatementSyntax(_syntaxTree,ifKeyword, openToken, condition, closeToken, statement, elIfClause, elseClause);
    }

    private ElseClauseSyntax? ParseElseClause()
    {
        if (Current.Kind != SyntaxKind.ElseKeyword)
        {
            return null;
        }

        var elseKeyword = Match(SyntaxKind.ElseKeyword);

        var blockStatement = ParseBlockStatement();

        return new ElseClauseSyntax(_syntaxTree,elseKeyword, blockStatement);
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

        return new ElseIfClauseSyntax(_syntaxTree,elseIfKeyword, condition, blockStatement);
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

        return new BlockStatementSyntax(_syntaxTree,openCurlyToken,proLangStatements.ToImmutable(),closeCurlyToken);
    }

    private StatementSyntax ParseExpressionStatement()
    {
        var expression = ParseExpression();

        return new ExpressionStatementSyntax(_syntaxTree,expression);
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
        
        return new HtmlDeclarationSyntax(_syntaxTree, nodes.ToImmutable());
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

        return new HtmlStatementSyntax(_syntaxTree,htmlStartTag,statements.ToImmutable(), htmlEndTag);
    }

    private HtmlStartTagSyntax ParseHtmlStartTag()
    {
        var leftAngle = Match(SyntaxKind.LessThanToken);
        var htmlKeyword = Match(SyntaxKind.IdentifierToken);
        var rightAngle = Match(SyntaxKind.GreaterThanToken);
        
        return new HtmlStartTagSyntax(_syntaxTree,leftAngle,htmlKeyword,rightAngle);
    }

    private HtmlEndTagSyntax ParseHtmlEndTag()
    {
        var closeToken = Match(SyntaxKind.OpenAngleForwardSlashToken);
        var htmlKeyword = Match(SyntaxKind.IdentifierToken);
        var rightAngle = Match(SyntaxKind.GreaterThanToken);

        return new HtmlEndTagSyntax(_syntaxTree,closeToken,htmlKeyword, rightAngle);
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

            left = new UnaryExpressionSyntax(_syntaxTree,operatorToken, operand);
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

            left = new BinaryExpressionSyntax(_syntaxTree,left, operatorToken, right);
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
                    return new AssignmentExpressionSyntax(_syntaxTree,identifierToken, operatorToken, expression);
                    
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
            case SyntaxKind.StringToken:
                return ParseStringLiteral();
            case SyntaxKind.IdentifierToken:
            default:
                
                return ParseNameOrCallExpression();
        }
    }

    private ExpressionSyntax ParseNameOrCallExpression()
    {
        if (Peek(0).Kind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.LeftParenthesisToken)
        {
            return ParseCallExpression();
        }

        return ParseNameExpression();
    }

    private ExpressionSyntax ParseCallExpression()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        var openParenthesisToken = Match(SyntaxKind.LeftParenthesisToken);
        var arguments = ParseArguments();
        var closeParenthesisToken = Match(SyntaxKind.RightParenthesisToken);

        return new CallExpressionSyntax(_syntaxTree,identifier,openParenthesisToken ,arguments, closeParenthesisToken);
    }

    private SeparatedSyntaxList<ExpressionSyntax> ParseArguments()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<SyntaxNode>();

        var parseNextArgument = true;

        while (parseNextArgument && Current.Kind != SyntaxKind.RightParenthesisToken && Current.Kind != SyntaxKind.EofToken)
        {
            var expression = ParseExpression();
            nodesAndSeparators.Add(expression);

            if (Current.Kind == SyntaxKind.CommaToken)
            {
                var comma = Match(SyntaxKind.CommaToken);
                nodesAndSeparators.Add(comma);
            }
            else
            {
                parseNextArgument = false;
            }
            
        }

        return new SeparatedSyntaxList<ExpressionSyntax>(nodesAndSeparators.ToImmutable());
    }

    private ExpressionSyntax ParseNameExpression()
    {
        var identifierToken = Match(SyntaxKind.IdentifierToken);

        return new NameExpressionSyntax(_syntaxTree,identifierToken);
    }

    private ExpressionSyntax ParseStringLiteral()
    {
        var stringToken = Match(SyntaxKind.StringToken);
        return new LiteralExpressionSyntax(_syntaxTree,stringToken);
    }

    private ExpressionSyntax ParseNumberLiteral()
    {
        var numberToken = Match(SyntaxKind.NumberToken);
        return new LiteralExpressionSyntax(_syntaxTree,numberToken);
    }

    private ExpressionSyntax ParseParenthesizedExpression()
    {
        var left = Match(SyntaxKind.LeftParenthesisToken);
        var expression = ParseExpression();
        var right = Match(SyntaxKind.RightParenthesisToken);

        return new ParenthesisExpressionSyntax(_syntaxTree,left, expression, right);
    }

    private ExpressionSyntax ParseBooleanLiteral()
    {
        var isTrue = Current.Kind == SyntaxKind.TrueKeyword;

        var keywordToken = isTrue ? Match(SyntaxKind.TrueKeyword) : Match(SyntaxKind.FalseKeyword);

        return new LiteralExpressionSyntax(_syntaxTree,keywordToken, isTrue);
    }

    private StatementSyntax ParseVariableStatement()
    {
        var letKeyword = Match(SyntaxKind.LetKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var typeClause = ParseOptionalTypeClause();
        var equalsToken = Match(SyntaxKind.EqualsToken);
        var expression = ParseExpression();

        return new VariableStatementSyntax(_syntaxTree,letKeyword,identifier,typeClause,equalsToken,expression);
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

        return new HtmlProLangBlockStatementSyntax(_syntaxTree,openCurlyToken, proLangStatements.ToImmutable(), closeCurlyToken);
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
            SyntaxKind.ContinueKeyword => ParseContinueStatement(),
            SyntaxKind.BreakKeyword => ParseBreakStatement(),
            SyntaxKind.ReturnKeyword => ParseReturnStatement(),
            _ => ParseExpressionStatement()
        };
        return statement;
        
    }

    private StatementSyntax ParseBreakStatement()
    {
        var keyword = Match(SyntaxKind.BreakKeyword);

        return new BreakStatementSyntax(_syntaxTree,keyword);
    }

    private StatementSyntax ParseContinueStatement()
    {
        var keyword = Match(SyntaxKind.ContinueKeyword);

        return new ContinueStatementSyntax(_syntaxTree,keyword);
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
        var body = ParseProLangStatement();

        return new ForStatementSyntax(_syntaxTree,forKeyword, openToken, identifier, equalToken, lowerBound, toKeyword, upBound,closeToken,
            body);
    }

    private StatementSyntax ParseWhileStatement()
    {
        var whileKeyword = Match(SyntaxKind.WhileKeyword);
        var openToken = Match(SyntaxKind.LeftParenthesisToken);
        var condition = ParseExpression();
        var closeToken = Match(SyntaxKind.RightParenthesisToken);

        var blockStatement = ParseProLangStatement();

        return new WhileStatementSyntax(_syntaxTree,whileKeyword, openToken, condition, closeToken, blockStatement);
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