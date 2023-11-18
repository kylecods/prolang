using ProLang.Syntax;

namespace ProLang.Parse;

internal sealed class Lexer
{
    private readonly string _text;

    private int _position;

    private List<string> _diagnostics = new ();

    public Lexer(string text)
    {
        _text = text;
    }

    public IEnumerable<string> Diagnostics => _diagnostics;

    private char Current => _position >= _text.Length ? '\0' : _text[_position];

    private void Next()
    {
        _position++;
    }

    public SyntaxToken NextToken()
    {
        if (_position >= _text.Length)
        {
            return new SyntaxToken(SyntaxKind.EofToken, _position, "\0", null!);
        }

        if (char.IsDigit(Current))
        {
            var start = _position;

            while (char.IsDigit(Current))
            {
                Next();
            }

            var length = _position - start;

            var text = _text.Substring(start, length);

            if (!int.TryParse(text, out var value))
            {
                _diagnostics.Add($"The number {_text} is not a valid Int32");
            }

            return new SyntaxToken(SyntaxKind.NumberToken, start, text, value);
        }

        if (char.IsWhiteSpace(Current))
        {
            var start = _position;

            while (char.IsWhiteSpace(Current))
            {
                Next();
            }

            var length = _position - start;

            var text = _text.Substring(start, length);

            return new SyntaxToken(SyntaxKind.WhitespaceToken, start, text, null!);
        }

        var syntaxToken = Current switch
        {
            '+' => new SyntaxToken(SyntaxKind.PlusToken,_position++,"+",null!),
            '-' => new SyntaxToken(SyntaxKind.MinusToken,_position++,"-",null!),
            '/' => new SyntaxToken(SyntaxKind.SlashToken,_position++,"/",null!),
            '*' => new SyntaxToken(SyntaxKind.StarToken,_position++,"*",null!),
            '=' => new SyntaxToken(SyntaxKind.EqualsToken,_position++,"=",null!),
            ';' => new SyntaxToken(SyntaxKind.SemiColonToken,_position++,";",null!),
            '(' => new SyntaxToken(SyntaxKind.LeftParenthesisToken,_position++,"(", null!),
            ')' => new SyntaxToken(SyntaxKind.RightParenthesisToken, _position++, ")", null!),
            _ => new SyntaxToken(SyntaxKind.BadToken,_position++,_text.Substring(_position -1, 1),null!)
        };

        if (syntaxToken.Kind == SyntaxKind.BadToken)
        {
            _diagnostics.Add($"ERROR: Unknown character: {Current}");

        }

        return syntaxToken;
    }
}