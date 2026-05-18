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

    private readonly SyntaxTree _syntaxTree;

    private readonly StringBuilder _stringBuilder = new(32);


    public Lexer(SyntaxTree syntaxTree)
    {
        _syntaxTree = syntaxTree;
        _text = syntaxTree.Text;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private TextLocation CreateErrorLocation(int length = 1)
    {
        var span = new TextSpan(_position, length);
        return new TextLocation(_text, span);
    }

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
                if (Current == '/')
                {
                    // Line comment - skip to end of line
                    while (Current != '\n' && Current != '\0')
                    {
                        _position++;
                    }
                    // Return whitespace token (comments are ignored)
                    _kind = SyntaxKind.WhitespaceToken;
                }
                else if (Current == '*')
                {
                    // Block comment - skip to */
                    _position++; // skip the *
                    while (true)
                    {
                        if (Current == '\0')
                        {
                            // Unterminated block comment
                            var span = new TextSpan(_start, 2);
                            var location = new TextLocation(_text, span);
                            _diagnostics.ReportUnterminatedString(location);
                            break;
                        }
                        if (Current == '*' && LookAhead == '/')
                        {
                            _position += 2; // skip */
                            break;
                        }
                        _position++;
                    }
                    // Return whitespace token (comments are ignored)
                    _kind = SyntaxKind.WhitespaceToken;
                }
                else if (Current != '>')
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
            case '[':
                _kind = SyntaxKind.LeftBracketToken;
                _position++;
                break;
            case ']':
                _kind = SyntaxKind.RightBracketToken;
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
            case ',':
                _kind = SyntaxKind.CommaToken;
                _position++;
                break;
            case ':' :
                _kind = SyntaxKind.ColonToken;
                _position++;
                break;
            case ';':
                _kind = SyntaxKind.SemiColonToken;
                _position++;
                break;
            case '.':
                _kind = SyntaxKind.DotToken;
                _position++;
                break;
            default:
                var c = Current;
                if (char.IsLetter(c))
                {
                    ReadIdentifierOrKeyword();
                }
                else if (char.IsWhiteSpace(c))
                {
                    ReadWhiteSpace();
                }
                else
                {
                    _diagnostics.ReportBadCharacter(CreateErrorLocation(1), c);
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

        return new SyntaxToken(_syntaxTree,_kind,_position,text,_value);
    }

    private void ReadString()
    {
        _position++;

        const int stackBufferSize = 256;
        Span<char> stackBuffer = stackalloc char[stackBufferSize];
        int bufferIndex = 0;
        bool overflowed = false;

        var done = false;

        while (!done)
        {
            switch (Current)
            {
                case '\0':
                case '\r':
                case '\n':
                    _diagnostics.ReportUnterminatedString(CreateErrorLocation(1));
                    done = true;
                    break;
                case '"':
                    if (LookAhead == '"')
                    {
                        if (!overflowed && bufferIndex < stackBufferSize)
                        {
                            stackBuffer[bufferIndex++] = Current;
                        }
                        else if (!overflowed)
                        {
                            overflowed = true;
                            _stringBuilder.Clear();
                            _stringBuilder.Append(stackBuffer[..bufferIndex]);
                            _stringBuilder.Append(Current);
                            bufferIndex++;
                        }
                        else
                        {
                            _stringBuilder.Append(Current);
                        }
                        _position += 2;
                    }
                    else
                    {
                        _position++;
                        done = true;
                    }
                    break;
                default:
                    if (!overflowed && bufferIndex < stackBufferSize)
                    {
                        stackBuffer[bufferIndex++] = Current;
                    }
                    else if (!overflowed)
                    {
                        overflowed = true;
                        _stringBuilder.Clear();
                        _stringBuilder.Append(stackBuffer[..bufferIndex]);
                        _stringBuilder.Append(Current);
                        bufferIndex++;
                    }
                    else
                    {
                        _stringBuilder.Append(Current);
                    }
                    _position++;
                    break;
            }
        }

        _kind = SyntaxKind.StringToken;
        _value = overflowed ? _stringBuilder.ToString() : new string(stackBuffer[..bufferIndex]);
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
        // Detect non-decimal prefix: 0x (hex), 0b (binary), 0o (octal)
        if (Current == '0')
        {
            var next = char.ToLowerInvariant(LookAhead);
            if (next == 'x' || next == 'b' || next == 'o')
            {
                _position += 2; // skip '0x' / '0b' / '0o'
                int radix = next == 'x' ? 16 : next == 'b' ? 2 : 8;

                while (IsValidDigitForRadix(Current, radix))
                    _position++;

                var length = _position - _start;
                var text = _text.ToString(_start, length);
                var digits = text.Substring(2); // strip prefix

                try
                {
                    _value = Convert.ToInt32(digits, radix);
                }
                catch
                {
                    _diagnostics.ReportInvalidNumber(CreateErrorLocation(length), text, TypeSymbol.Int);
                    _value = 0;
                }

                _kind = SyntaxKind.NumberToken;
                return;
            }
        }

        // Read integer part
        while (char.IsDigit(Current))
            _position++;

        // Detect float literal: digits '.' digits [suffix]
        if (Current == '.' && char.IsDigit(Peek(1)))
        {
            _position++; // consume '.'
            while (char.IsDigit(Current))
                _position++;

            var numericEnd = _position; // end of numeric digits (before any suffix)

            // Optional suffix: f → float32, f32 → float32, f64 → float64
            var floatType = TypeSymbol.Float64;
            if (char.ToLowerInvariant(Current) == 'f')
            {
                _position++; // consume 'f'
                if (Current == '3' && Peek(1) == '2')
                {
                    _position += 2;
                    floatType = TypeSymbol.Float32;
                }
                else if (Current == '6' && Peek(1) == '4')
                {
                    _position += 2;
                    floatType = TypeSymbol.Float64;
                }
                else
                {
                    floatType = TypeSymbol.Float32; // bare 'f' = float32
                }
            }

            var floatLength = _position - _start;
            var numericText = _text.ToString(_start, numericEnd - _start);

            if (floatType == TypeSymbol.Float32)
            {
                if (!float.TryParse(numericText, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var fv))
                    _diagnostics.ReportInvalidNumber(CreateErrorLocation(floatLength), numericText, TypeSymbol.Float32);
                _value = fv;
            }
            else
            {
                if (!double.TryParse(numericText, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    _diagnostics.ReportInvalidNumber(CreateErrorLocation(floatLength), numericText, TypeSymbol.Float64);
                _value = dv;
            }

            _kind = SyntaxKind.NumberToken;
            return;
        }

        // Decimal integer path
        var decLength = _position - _start;
        var decText = _text.ToString(_start, decLength);

        if (!int.TryParse(decText, out var decValue))
            _diagnostics.ReportInvalidNumber(CreateErrorLocation(decLength), decText, TypeSymbol.Int);

        _value = decValue;
        _kind = SyntaxKind.NumberToken;
    }

    private static bool IsValidDigitForRadix(char c, int radix) => radix switch
    {
        16 => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'),
        2  => c == '0' || c == '1',
        8  => c >= '0' && c <= '7',
        _  => false,
    };

    private void ReadIdentifierOrKeyword()
    {
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            _position++;
        }

        var length = _position - _start;
        var text = _text.ToString(_start, length);

        _kind = SyntaxFacts.GetKeywordKind(text);
    }
}