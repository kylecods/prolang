namespace ProLang.Messages;
public class MessageHandler {

    private Message _message;

    private readonly List<MessageListener> _listeners;

    public MessageHandler() {
        _listeners = new List<MessageListener>();
    }

    public void AddMessageListener(MessageListener listener) {
        _listeners.Add(listener);
    }
    public void RemoveMessageListener(MessageListener listener){
        _listeners.Remove(listener);
    }

    public void SendMessage(Message message) {
        _message = message;
        NotifyListeners();
    }

    private void NotifyListeners()
    {
        foreach (var listener in _listeners){
            listener.MesssageReceived(_message);
        }
    }
}