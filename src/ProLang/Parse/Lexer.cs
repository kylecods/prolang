using System.Text;
using ProLang.Symbols;
using ProLang.Syntax;
using ProLang.Text;

namespace ProLang.Parse;

internal sealed class Lexer
{
    private readonly SourceText _text;
    
    private int _position;
    
    private int _start;
    
    private SyntaxKind _kind;
    
    private object _value;
    
    private DiagnosticBag _diagnostics = new ();


    public Lexer(SourceText text)
    {
        _text = text;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private char Current => Peek(0);

    private char LookAhead => Peek(1);

    private char Peek(int offset)
    {
        var index = _position + offset;

        if (index >= _text.Length)
        {
            return '\0';
        }

        return _text[index];
    }

    public SyntaxToken Lex()
    {
        _start = _position;
        _kind = SyntaxKind.BadToken;
        _value = null!;

        switch (Current)
        {
            case '\0':
                _kind = SyntaxKind.EofToken;
                break;
            case '+':
                _position++;
                if (Current != '=')
                {
                    _kind = SyntaxKind.PlusToken;
                }
                else
                {
                    _position++;
                    _kind = SyntaxKind.PlusEqualsToken;
                }
                break;
            case '-':
                _position++;
                if (Current != '=')
                {
                    _kind = SyntaxKind.MinusToken;
                }
                else
                {
                    _position++;
                    _kind = SyntaxKind.MinusEqualsToken;
                }
                break;
            case '/':
                _position++;
                if (Current != '>')
                {
                    _kind = SyntaxKind.SlashToken;
                }
                else
                {
                    _position++;
                    _kind = SyntaxKind.ForwardSlashCloseAngleToken;
                }
                break;
            case '*':
                _position++;
                if (Current != '=')
                {
                    _kind = SyntaxKind.StarToken;
                }
                else
                {
                    _position++;
                    _kind = SyntaxKind.StarEqualsToken;
                }
                break;
            case '%':
                _kind = SyntaxKind.PercentageToken;
                _position++;
                break;
            case '<':
                if (LookAhead == '/')
                {
                    _kind = SyntaxKind.OpenAngleForwardSlashToken;
                    _position+=2;

                }else if (LookAhead == '=')
                {
                    _kind = SyntaxKind.LessThanEqualToken;
                    _position+=2;
                }
                else
                {
                    _position++;
                    _kind = SyntaxKind.LessThanToken;
                }

                break;
            case '>':
                _position++;
                if (Current != '=')
                {
                    _kind = SyntaxKind.GreaterThanToken;
                }
                else
                {
                    _kind = SyntaxKind.GreaterThanEqualToken;
                    _position++;
                    
                }
                break;
            case '=':
                _position++;
                if (Current != '=')
                {
                    _kind = SyntaxKind.EqualsToken;
                }
                else
                {
                    _kind = SyntaxKind.EqualsEqualsToken;
                    _position++;
                }
                break;
            case '(':
                _kind = SyntaxKind.LeftParenthesisToken;
                _position++;
                break;
            case ')':
                _kind = SyntaxKind.RightParenthesisToken;
                _position++;
                break;
            case '&':
                _position++;
                if (Current != '&')
                {
                    _kind = SyntaxKind.AmpersandToken;
                }
                else
                {
                    _kind = SyntaxKind.AmpersandAmpersandToken;
                    _position++;
                }
                break;
            case '|':
                _position++;
                if (Current != '|')
                {
                    _kind = SyntaxKind.PipeToken;
                }
                else
                {
                    _kind = SyntaxKind.PipePipeToken;
                    _position++;
                }
                break;
            case '!':
                _position++;
                if (Current != '=')
                {
                    _kind = SyntaxKind.BangToken;
                }
                else
                {
                    _kind = SyntaxKind.BangEqualsToken;
                    _position++;
                }
                break;
            case '0':case '1':case '2':case '3':case '4':
            case '5':case '6':case '7':case '8':case '9':
                ReadNumberToken();
                break;
            case ' ': case '\t':
            case '\n': case '\r':
                ReadWhiteSpace();
                break;
            case '$' :
                _position++;
                if (Current != '{')
                {
                    _kind = SyntaxKind.DollarToken;
                }
                else
                {
                    _kind = SyntaxKind.DollarCurlyToken;
                    _position++;
                }
                break;
            case '{':
                _kind = SyntaxKind.LeftCurlyToken;
                _position++;
                break;
            case '}':
                _kind = SyntaxKind.RightCurlyToken;
                _position++;
                break;
            case '"':
                ReadString();
                break;
            case '~':
                _kind = SyntaxKind.TildeToken;
                _position++;
                break;
            case '^':
                _kind = SyntaxKind.HatToken;
                _position++;
                break;
            
            default:
                if (char.IsLetter(Current))
                {
                    ReadIdentifierOrKeyword();
                }else if (char.IsWhiteSpace(Current))
                {
                    ReadWhiteSpace();
                }
                else
                {
                    _diagnostics.ReportBadCharacter(_position, Current);

                    _position++;
                }
                break;
        }

        var length = _position - _start;
        var text = SyntaxFacts.GetText(_kind);

        if (text == null)
        {
            text = _text.ToString(_start, length);
        }

        return new SyntaxToken(_kind,_position,text,_value);
    }

    private void ReadString()
    {
        _position++;

        var sb = new StringBuilder();

        var done = false;

        while (!done)
        {
            switch (Current)
            {
                case '\0':
                case '\r':
                case '\n':
                    var span = new TextSpan(_start, 1);
                    _diagnostics.ReportUnterminatedString(span);
                    done = true;
                    break;
                case '"':
                    if (LookAhead == '"')
                    {
                        sb.Append(Current);
                        _position += 2;
                    }
                    else
                    {
                        _position++;
                        done = true;
                    }
                    break;
                default:
                    sb.Append(Current);
                    _position++;
                    break;
            }
        }

        _kind = SyntaxKind.StringToken;
        _value = sb.ToString();
    }

    private void ReadWhiteSpace()
    {
        while (char.IsWhiteSpace(Current))
        {
            _position++;
        }

        _kind = SyntaxKind.WhitespaceToken;
    }

    private void ReadNumberToken()
    {
        while (char.IsDigit(Current))
        {
            _position++;
        }

        var length = _position - _start;
        var text = _text.ToString(_start, length);

        if (!int.TryParse(text, out var value))
        {
            _diagnostics.ReportInvalidNumber(new TextSpan(_start,length),text,TypeSymbol.Int);
        }

        _value = value;
        _kind = SyntaxKind.NumberToken;
    }

    private void ReadIdentifierOrKeyword()
    {
        while (char.IsLetter(Current))
        {
            _position++;
        }

        var length = _position - _start;
        var text = _text.ToString(_start, length);

        _kind = SyntaxFacts.GetKeywordKind(text);
    }
}