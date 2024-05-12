using ProLang.Text;

namespace ProLang.Parse;

public sealed class Diagnostic
{
    public Diagnostic(TextLocation location, string message)
    {
        Location = location;
        Message = message;
    }
    
    public TextLocation Location { get; }
    
    public string Message { get; }

    public override string ToString() => Message;
}