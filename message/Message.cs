namespace ProLang.Messages;
public class Message
{
    private readonly MessageType _type;

    private readonly object _body;

    public Message(MessageType type, object body)
    {

        _type = type;

        _body = body;
    }

    public MessageType Type
    {
        get { return _type; }
    }

    public object Body
    {
        get { return _body; }
    }
}