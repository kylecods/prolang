using ProLang.FrontEnd;

public class Token 
{
    protected TokenType _type;

    protected string _text;

    protected object _value;

    protected Source _source;

    protected int _lineNum;

    protected int _position;

    public TokenType Type {
        get { return _type; }
    }

    public string Text {
        get { return _text; }
    }

    public object Value {
        get { return _value; }
    }

    public Source Source {
        get { return _source; }
    }

    public int LineNum {
        get { return _lineNum; }
    }

    public int Position {
        get { return _position; }
    }

    public Token(Source source){
        _source = source;

        _lineNum = source.LineNum;

        _position = source.Position;
    }

    protected virtual void Extract() {
        _text = CurrentChar().ToString();
        _value = null;
        NextChar();
    }

    protected char CurrentChar() {
        return _source.CurrentChar();
    }

    protected char NextChar() {
        return _source.NextChar();
    }

    protected char PeekChar() {
        return _source.PeekChar();
    }

}