using ProLang.Messages;

namespace ProLang.FrontEnd;

public abstract class Parser : MessageProducer
{
    protected readonly MessageHandler _messageHandler;

    protected readonly Scanner _scanner;

    public Parser(Scanner scanner){

        _scanner = scanner;

        _messageHandler = new MessageHandler();
    }
    
    public abstract void Parse();

    protected abstract int ErrorCount();

    public Token CurrentToken() => _scanner.CurrentToken;

    public Token NextToken() => _scanner.NextToken();


    public void AddMessageListener(MessageListener listener)
    {
        _messageHandler.AddMessageListener(listener);
    }

    public void RemoveMessageListener(MessageListener listener)
    {
        _messageHandler.RemoveMessageListener(listener);
    }

    public void SendMessage(Message message)
    {
        _messageHandler.SendMessage(message);
    }
}