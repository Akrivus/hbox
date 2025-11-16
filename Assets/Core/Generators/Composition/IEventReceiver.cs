public interface IEventReceiver
{
    void Receive(string message, bool initialOnly = true);
}