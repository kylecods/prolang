using System.Collections.Immutable;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Parse;

public sealed class Parser
{
    private DiagnosticBag _diagnostics = new();
    
    private readonly SourceText _text;

    private readonly ImmutableArray<SyntaxToken> _tokens;

    private int _position;

    private readonly SyntaxTree _syntaxTree;

    public Parser(SyntaxTree syntaxTree)
    {
        var tokens = new List<SyntaxToken>(256);

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
            return _tokens[_tokens.Length - 1];
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
        if (Current.Kind == SyntaxKind.ImportKeyword)
        {
            return ParseImportDeclaration();
        }

        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            return ParseHtmlDeclaration();
        }

        if (Current.Kind == SyntaxKind.FunctionKeyword)
        {
            return ParseFunctionDeclaration();
        }

        if (Current.Kind == SyntaxKind.StructKeyword)
        {
            return ParseStructDeclaration();
        }

        return ParseGlobalStatement();
    }

    private ImportDeclarationSyntax ParseImportDeclaration()
    {
        var importKeyword = Match(SyntaxKind.ImportKeyword);
        var pathToken = Match(SyntaxKind.StringToken);
        return new ImportDeclarationSyntax(_syntaxTree, importKeyword, pathToken);
    }

    private DeclarationSyntax ParseFunctionDeclaration()
    {
        var functionKeyword = Match(SyntaxKind.FunctionKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);

        SyntaxToken? lessThanToken = null;
        var typeParameters = new SeparatedSyntaxList<SyntaxToken>(ImmutableArray<SyntaxNode>.Empty);
        SyntaxToken? greaterThanToken = null;

        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            lessThanToken = Match(SyntaxKind.LessThanToken);
            typeParameters = ParseTypeParameterList();
            greaterThanToken = Match(SyntaxKind.GreaterThanToken);
        }

        var openParenthesisToken = Match(SyntaxKind.LeftParenthesisToken);
        var parameters = ParseParameterList();
        var closeParenthesisToken = Match(SyntaxKind.RightParenthesisToken);
        var type = ParseOptionalTypeClause();
        var body = ParseBlockStatement();

        return new FunctionDeclarationSyntax(_syntaxTree, functionKeyword, identifier,
            lessThanToken, typeParameters, greaterThanToken,
            openParenthesisToken, parameters, closeParenthesisToken, type, (BlockStatementSyntax)body);
    }

    private StructDeclarationSyntax ParseStructDeclaration()
    {
        var structKeyword = Match(SyntaxKind.StructKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);

        SyntaxToken? lessThanToken = null;
        var typeParameters = new SeparatedSyntaxList<SyntaxToken>(ImmutableArray<SyntaxNode>.Empty);
        SyntaxToken? greaterThanToken = null;

        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            lessThanToken = Match(SyntaxKind.LessThanToken);
            typeParameters = ParseTypeParameterList();
            greaterThanToken = Match(SyntaxKind.GreaterThanToken);
        }

        var openCurlyToken = Match(SyntaxKind.LeftCurlyToken);

        var fields = ImmutableArray.CreateBuilder<FieldDeclarationSyntax>();

        while (Current.Kind != SyntaxKind.RightCurlyToken && Current.Kind != SyntaxKind.EofToken)
        {
            var startToken = Current;
            var fieldIdentifier = Match(SyntaxKind.IdentifierToken);
            var fieldType = ParseTypeClause();
            // Accept both semicolons and commas as field separators
            if (Current.Kind == SyntaxKind.SemiColonToken || Current.Kind == SyntaxKind.CommaToken)
                NextToken();
            fields.Add(new FieldDeclarationSyntax(_syntaxTree, fieldIdentifier, fieldType));
            // Guard: if no progress was made, skip the offending token to avoid infinite loop
            if (Current == startToken)
                NextToken();
        }

        var closeCurlyToken = Match(SyntaxKind.RightCurlyToken);

        return new StructDeclarationSyntax(_syntaxTree, structKeyword, identifier, lessThanToken, typeParameters, greaterThanToken, openCurlyToken, fields.ToImmutable(), closeCurlyToken);
    }

    private SeparatedSyntaxList<SyntaxToken> ParseTypeParameterList()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<SyntaxNode>();

        var parseNextParameter = true;

        while (parseNextParameter && Current.Kind != SyntaxKind.GreaterThanToken && Current.Kind != SyntaxKind.EofToken)
        {
            var parameter = Match(SyntaxKind.IdentifierToken);
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

        return new SeparatedSyntaxList<SyntaxToken>(nodesAndSeparators.ToImmutable());
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
        var type = ParseTypeSyntax();

        return new TypeClauseSyntax(_syntaxTree, colonToken, type);
    }

    private TypeSyntax ParseTypeSyntax()
    {
        var baseType = ParseTypeBase();
        return ParseArraySuffix(baseType);
    }

    private TypeSyntax ParseTypeBase()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            var lessThan = Match(SyntaxKind.LessThanToken);
            var arguments = ParseGenericArguments();
            var greaterThan = Match(SyntaxKind.GreaterThanToken);
            return new GenericTypeSyntax(_syntaxTree, identifier, lessThan, arguments, greaterThan);
        }

        return new NameTypeSyntax(_syntaxTree, identifier);
    }

    private TypeSyntax ParseArraySuffix(TypeSyntax baseType)
    {
        var type = baseType;
        while (Current.Kind == SyntaxKind.LeftBracketToken)
        {
            var openBracket = Match(SyntaxKind.LeftBracketToken);
            var closeBracket = Match(SyntaxKind.RightBracketToken);
            type = new ArrayTypeSyntax(_syntaxTree, type, openBracket, closeBracket);
        }
        return type;
    }

    private SeparatedSyntaxList<TypeSyntax> ParseGenericArguments()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<SyntaxNode>();

        var parseNextArgument = true;

        while (parseNextArgument && Current.Kind != SyntaxKind.GreaterThanToken && Current.Kind != SyntaxKind.EofToken)
        {
            var type = ParseTypeSyntax();
            nodesAndSeparators.Add(type);

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

        return new SeparatedSyntaxList<TypeSyntax>(nodesAndSeparators.ToImmutable());
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

        // Support "else if" as syntactic sugar for "else { if ... }"
        if (Current.Kind == SyntaxKind.IfKeyword)
        {
            var ifStatement = ParseIfStatement();
            var statements = ImmutableArray.Create<StatementSyntax>(ifStatement);

            // Create synthetic tokens for the block wrapper
            var openBrace = new SyntaxToken(_syntaxTree, SyntaxKind.LeftCurlyToken, 0, "", null);
            var closeBrace = new SyntaxToken(_syntaxTree, SyntaxKind.RightCurlyToken, 0, "", null);

            var blockStatement = new BlockStatementSyntax(_syntaxTree, openBrace, statements, closeBrace);
            return new ElseClauseSyntax(_syntaxTree, elseKeyword, blockStatement);
        }

        var regularBlock = ParseBlockStatement();

        return new ElseClauseSyntax(_syntaxTree,elseKeyword, regularBlock);
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
            // Check for cast expression (as operator has high precedence)
            if (Current.Kind == SyntaxKind.AsKeyword)
            {
                var asKeyword = NextToken();
                var typeSyntax = ParseTypeSyntax();
                var targetType = new TypeClauseSyntax(_syntaxTree, null!, typeSyntax);
                left = new CastExpressionSyntax(_syntaxTree, left, asKeyword, targetType);
            }
            else
            {
                var precedence = Current.Kind.GetBinaryOperatorPrecedence();
                if (precedence == 0 || precedence <= parentPrecedence)
                    break;
                var operatorToken = NextToken();
                var right = ParseBinaryExpression(precedence);

                left = new BinaryExpressionSyntax(_syntaxTree,left, operatorToken, right);
            }
        }

        return left;
    }

    private ExpressionSyntax ParseAssignmentExpression()
    {
        var left = ParseBinaryExpression();

        if (Current.Kind == SyntaxKind.EqualsToken ||
            Current.Kind == SyntaxKind.PlusEqualsToken ||
            Current.Kind == SyntaxKind.MinusEqualsToken ||
            Current.Kind == SyntaxKind.StarEqualsToken ||
            Current.Kind == SyntaxKind.SlashEqualsToken)
        {
            var operatorToken = NextToken();
            var right = ParseAssignmentExpression();
            return new AssignmentExpressionSyntax(_syntaxTree, left, operatorToken, right);
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
                return ParseBooleanLiteral();
            }
            case SyntaxKind.NullKeyword:
            {
                return ParseNullLiteral();
            }
            case SyntaxKind.LeftParenthesisToken:
            {
                return ParseParenthesizedExpression();
            }
            case SyntaxKind.NumberToken:
                return ParseNumberLiteral(); 
            case SyntaxKind.StringToken:
                return ParseStringLiteral();
            case SyntaxKind.LeftBracketToken:
                return ParseArrayLiteral();
            case SyntaxKind.LeftCurlyToken:
                return ParseMapLiteral();
            case SyntaxKind.IdentifierToken:
                if (Peek(1).Kind == SyntaxKind.LeftCurlyToken)
                {
                    return ParseStructCreationExpression();
                }
                if (Peek(1).Kind == SyntaxKind.LessThanToken
                    && LookAheadGenericEnd(SyntaxKind.LeftCurlyToken, out _))
                {
                    return ParseStructCreationExpression();
                }
                return ParsePostFixExpression();
            default:
                return ParsePostFixExpression();
        }
    }

    private ExpressionSyntax ParseArrayLiteral()
    {
        var leftBracket = Match(SyntaxKind.LeftBracketToken);
        var elements = ParseArrayElements();
        var rightBracket = Match(SyntaxKind.RightBracketToken);
        return new ArrayExpressionSyntax(_syntaxTree, leftBracket, elements, rightBracket);
    }

    private SeparatedSyntaxList<ExpressionSyntax> ParseArrayElements()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<SyntaxNode>();

        var parseNextElement = true;

        while (parseNextElement && Current.Kind != SyntaxKind.RightBracketToken && Current.Kind != SyntaxKind.EofToken)
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
                parseNextElement = false;
            }
        }

        return new SeparatedSyntaxList<ExpressionSyntax>(nodesAndSeparators.ToImmutable());
    }

    private ExpressionSyntax ParseMapLiteral()
    {
        var leftCurly = Match(SyntaxKind.LeftCurlyToken);
        var entries = ParseMapEntries();
        var rightCurly = Match(SyntaxKind.RightCurlyToken);
        return new MapExpressionSyntax(_syntaxTree, leftCurly, entries, rightCurly);
    }

    private StructCreationExpressionSyntax ParseStructCreationExpression()
    {
        var typeName = Match(SyntaxKind.IdentifierToken);

        var typeArguments = ImmutableArray<TypeSyntax>.Empty;
        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            Match(SyntaxKind.LessThanToken);
            typeArguments = ParseTypeArgumentList();
            Match(SyntaxKind.GreaterThanToken);
        }

        var openCurlyToken = Match(SyntaxKind.LeftCurlyToken);

        var initializers = ImmutableArray.CreateBuilder<FieldInitializerSyntax>();

        var parseNextField = true;

        while (parseNextField && Current.Kind != SyntaxKind.RightCurlyToken && Current.Kind != SyntaxKind.EofToken)
        {
            var fieldName = Match(SyntaxKind.IdentifierToken);
            var colonToken = Match(SyntaxKind.ColonToken);
            var value = ParseExpression();
            initializers.Add(new FieldInitializerSyntax(_syntaxTree, fieldName, colonToken, value));

            if (Current.Kind == SyntaxKind.CommaToken)
            {
                var comma = Match(SyntaxKind.CommaToken);
            }
            else
            {
                parseNextField = false;
            }
        }

        var closeCurlyToken = Match(SyntaxKind.RightCurlyToken);

        return new StructCreationExpressionSyntax(_syntaxTree, typeName, typeArguments, openCurlyToken, initializers.ToImmutable(), closeCurlyToken);
    }

    private SeparatedSyntaxList<MapEntrySyntax> ParseMapEntries()
    {
        var nodesAndSeparators = ImmutableArray.CreateBuilder<SyntaxNode>();

        var parseNextEntry = true;

        while (parseNextEntry && Current.Kind != SyntaxKind.RightCurlyToken && Current.Kind != SyntaxKind.EofToken)
        {
            var entry = ParseMapEntry();
            nodesAndSeparators.Add(entry);

            if (Current.Kind == SyntaxKind.CommaToken)
            {
                var comma = Match(SyntaxKind.CommaToken);
                nodesAndSeparators.Add(comma);
            }
            else
            {
                parseNextEntry = false;
            }
        }

        return new SeparatedSyntaxList<MapEntrySyntax>(nodesAndSeparators.ToImmutable());
    }

    private MapEntrySyntax ParseMapEntry()
    {
        var key = ParseExpression();
        var colon = Match(SyntaxKind.ColonToken);
        var value = ParseExpression();
        return new MapEntrySyntax(_syntaxTree, key, colon, value);
    }

    private ExpressionSyntax ParsePostFixExpression()
    {
        var expression = ParseNameOrCallExpression();

        while (true)
        {
            if (Current.Kind == SyntaxKind.LeftBracketToken)
            {
                var leftBracket = Match(SyntaxKind.LeftBracketToken);
                var index = ParseExpression();
                var rightBracket = Match(SyntaxKind.RightBracketToken);
                expression = new IndexExpressionSyntax(_syntaxTree, expression, leftBracket, index, rightBracket);
            }
            else if (Current.Kind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
            {
                if (Peek(2).Kind == SyntaxKind.LeftParenthesisToken)
                {
                    var dotToken = Match(SyntaxKind.DotToken);
                    var methodName = Match(SyntaxKind.IdentifierToken);
                    var openParen = Match(SyntaxKind.LeftParenthesisToken);
                    var arguments = ParseArguments();
                    var closeParen = Match(SyntaxKind.RightParenthesisToken);
                    expression = new MethodCallExpressionSyntax(_syntaxTree, expression, dotToken, methodName, openParen, arguments, closeParen);
                }
                else
                {
                    var dotToken = Match(SyntaxKind.DotToken);
                    var fieldName = Match(SyntaxKind.IdentifierToken);
                    expression = new FieldAccessExpressionSyntax(_syntaxTree, expression, dotToken, fieldName);
                }
            }
            else
            {
                break;
            }
        }

        return expression;
    }

    private ExpressionSyntax ParseNameOrCallExpression()
    {
        if (Peek(0).Kind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.LeftParenthesisToken)
            return ParseCallExpression();

        if (Peek(0).Kind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.LessThanToken
            && LookAheadGenericEnd(SyntaxKind.LeftParenthesisToken, out _))
            return ParseCallExpression();

        return ParseNameExpression();
    }

    // Returns true if: after current identifier + '<', there is a balanced '>' followed by 'followedBy'
    private bool LookAheadGenericEnd(SyntaxKind followedBy, out int closeOffset)
    {
        closeOffset = 0;
        int i = 2; // skip identifier(0) and '<'(1)
        int depth = 1;
        while (true)
        {
            var kind = Peek(i).Kind;
            if (kind == SyntaxKind.EofToken) return false;
            if (kind == SyntaxKind.LessThanToken) depth++;
            else if (kind == SyntaxKind.GreaterThanToken)
            {
                depth--;
                if (depth == 0)
                {
                    closeOffset = i;
                    return Peek(i + 1).Kind == followedBy;
                }
            }
            i++;
            if (i > 32) return false; // safety limit
        }
    }

    private ExpressionSyntax ParseCallExpression()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);

        var typeArguments = ImmutableArray<TypeSyntax>.Empty;
        if (Current.Kind == SyntaxKind.LessThanToken)
        {
            Match(SyntaxKind.LessThanToken);
            typeArguments = ParseTypeArgumentList();
            Match(SyntaxKind.GreaterThanToken);
        }

        var openParenthesisToken = Match(SyntaxKind.LeftParenthesisToken);
        var arguments = ParseArguments();
        var closeParenthesisToken = Match(SyntaxKind.RightParenthesisToken);

        return new CallExpressionSyntax(_syntaxTree, identifier, typeArguments, openParenthesisToken, arguments, closeParenthesisToken);
    }

    private ImmutableArray<TypeSyntax> ParseTypeArgumentList()
    {
        var args = ImmutableArray.CreateBuilder<TypeSyntax>();
        while (Current.Kind != SyntaxKind.GreaterThanToken && Current.Kind != SyntaxKind.EofToken)
        {
            args.Add(ParseTypeSyntax());
            if (Current.Kind == SyntaxKind.CommaToken)
                NextToken();
            else
                break;
        }
        return args.ToImmutable();
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

    private ExpressionSyntax ParseNullLiteral()
    {
        var nullToken = Match(SyntaxKind.NullKeyword);
        return new LiteralExpressionSyntax(_syntaxTree, nullToken, null);
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

        SyntaxToken identifier;
        SyntaxToken equalToken;

        // Check if this is a variable declaration: for(let i = 0 to 10)
        if (Current.Kind == SyntaxKind.LetKeyword)
        {
            NextToken(); // consume 'let'
            identifier = Match(SyntaxKind.IdentifierToken);

            // Check for type annotation (optional): for(let i: int = 0 to 10)
            if (Current.Kind == SyntaxKind.ColonToken)
            {
                NextToken(); // consume ':'
                ParseTypeClause(); // parse and discard the type (we only care about the identifier)
            }

            equalToken = Match(SyntaxKind.EqualsToken);
        }
        else
        {
            // Original behavior: for(i = 0 to 10) where i is pre-declared
            identifier = Match(SyntaxKind.IdentifierToken);
            equalToken = Match(SyntaxKind.EqualsToken);
        }

        var lowerBound = ParseExpression();
        var toKeyword = Match(SyntaxKind.ToKeyword);
        var upBound = ParseExpression();
        var closeToken = Match(SyntaxKind.RightParenthesisToken);
        var body = ParseProLangStatement();

        return new ForStatementSyntax(_syntaxTree, forKeyword, openToken, identifier, equalToken, lowerBound, toKeyword, upBound, closeToken,
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