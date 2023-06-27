
using ProLang.Messages;

namespace ProLang.FrontEnd;

public class Source : MessageProducer, IDisposable
{
    public static readonly char EOF = '\0';

    public static readonly char EOL = '\n';

    private readonly MessageHandler _handler;

    private readonly StreamReader _reader;
    
    private int _lineNum;

    private int _currentPos;

    private string _line;

    public string Line { get => _line; }
    public int LineNum { get => _lineNum;  }

    public int Position { get => _currentPos; }

    public Source(string sourceFile){
        _reader = new StreamReader(sourceFile);
        _lineNum = 0;
        _currentPos = -2;
        _handler = new MessageHandler();
    }

    public char CurrentChar() {
        if(_currentPos == -2){
            Readline();
            return NextChar();
        }

        if(_line == null) return EOF;

        if(_currentPos == -1 || _currentPos == _line.Length) return EOL;

        if(_currentPos > _line.Length){
            Readline();
            return NextChar();
        }

        return _line[_currentPos];
    }

    public char NextChar(){
        ++_currentPos;
        return CurrentChar();
    }

    public char PeekChar() {
        CurrentChar();

        if(_line == null) return EOF;

        int nextPos = _currentPos + 1;

        return nextPos < _line.Length ? _line[nextPos] : EOL;
    }

    private void Readline(){

        _line = _reader.ReadLine();

        _currentPos = -1;

        if(_line != null) {
            ++_lineNum;
        }

        if(_line != null) {
           SendMessage(new Message(MessageType.SOURCE_LINE,
            new object [] {
                _lineNum,
                _line
            }
           ));
        }

    }

    public void AddMessageListener(MessageListener listener)
    {
        _handler.AddMessageListener(listener);
    }

    public void RemoveMessageListener(MessageListener listener)
    {
        _handler.RemoveMessageListener(listener);
    }

    public void SendMessage(Message message)
    {
        _handler.SendMessage(message);
    }

    public void Dispose()
    {
        _reader.Close();
    }
}
