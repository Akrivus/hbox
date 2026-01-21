using System;

[Serializable]
public struct UiEvent
{
    public string channelCode;
    public string message;
    public float lifetimeSeconds;

    public DateTime timestamp;
    public DateTime? countdown;

    public bool IsComplete => !countdown.HasValue || DateTime.Now >= countdown.Value;
}

public static class UiEventBus
{
    public static event Action<UiEvent> OnEvent;

    public static void Publish(ChatManagerContext context, string message, float lifetimeSeconds = 10)
    {
        OnEvent?.Invoke(new UiEvent
        {
            channelCode = context.Key,
            message = message.Truncate(100),
            lifetimeSeconds = lifetimeSeconds,
            timestamp = DateTime.Now
        });
    }

    public static void Publish(ChatManagerContext context, DateTime countdown, float lifetimeSeconds = 0)
    {
        OnEvent?.Invoke(new UiEvent
        {
            channelCode = context.Key,
            message = string.Empty,
            lifetimeSeconds = lifetimeSeconds,
            timestamp = DateTime.Now,
            countdown = countdown
        });
    }

    private static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var lines = value.Split(new char[] { '\n' });
        if (lines.Length > 1)
            value = lines[0] + " ...";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }
}
