namespace ProLang.FrontEnd;

public class EOFToken : Token {
    public EOFToken(Source source, TokenType tokenType) : base(source) {}

    protected override void Extract(){}
}