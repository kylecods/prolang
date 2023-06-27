namespace ProLang
{
    public class Source : IDisposable
    {
        public static readonly char EOF = '\0';

        public static readonly char EOL = '\n';

        private readonly StreamReader _reader;
        
        private int _lineNum;

        private int _currentPos;

        private string? _line;

        public Source(string sourceFile){
            _reader = new StreamReader(sourceFile);
            _lineNum = 0;
            _currentPos = -2;
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

        private void Readline(){
  
            _line = _reader.ReadLine();

            _currentPos = -1;

            if(_line != null) {
                ++_lineNum;
            }

            if(_line != null) {
                //TODO: send message.
            }

        }

        public void Dispose()
        {
            _reader.Close();
        }

    }
}
