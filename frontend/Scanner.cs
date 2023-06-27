namespace ProLang.FrontEnd;
public abstract class Scanner {
    protected Source _source;

    private Token _currentToken;

    public Scanner(Source source) {
        _source = source;
    }

    public Token NextToken(){
        _currentToken = ExtractToken();
        return _currentToken;
    }

    public Token CurrentToken => _currentToken;

    protected abstract Token ExtractToken();

    public char CurrentChar() {
        return _source.CurrentChar();
    }

    public char NextChar() {
        return _source.NextChar();
    }


}