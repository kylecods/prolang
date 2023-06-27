namespace ProLang.Messages;
public interface MessageProducer{
    public void AddMessageListener(MessageListener listener);

    public void RemoveMessageListener(MessageListener listener);

    public void SendMessage(Message message);
}