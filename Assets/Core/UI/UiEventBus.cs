using System;
using UnityEngine;

[Serializable]
public struct UiEvent
{
    public string ChannelCode;
    public string Message;
    public float LifetimeInSeconds;
    public bool IsPinned;

    public DateTime Timestamp;
    public DateTime? Countdown;

    public GameObject obj;

    public DateTime DateTime => (Countdown ?? Timestamp).AddSeconds(LifetimeInSeconds);
}

public static class UiEventBus
{
    public static event Action<UiEvent> OnEvent;

    public static void Publish(ChatManagerContext context, string message, float lifetimeSeconds = 10)
    {
        OnEvent?.Invoke(new UiEvent
        {
            ChannelCode = context.Key,
            Message = message.Truncate(100),
            LifetimeInSeconds = lifetimeSeconds,
            Timestamp = DateTime.Now
        });
    }

    public static void Publish(ChatManagerContext context, DateTime countdown, float lifetimeSeconds = 0)
    {
        OnEvent?.Invoke(new UiEvent
        {
            ChannelCode = context.Key,
            Message = string.Empty,
            LifetimeInSeconds = lifetimeSeconds,
            Timestamp = DateTime.Now,
            Countdown = countdown
        });
    }

    public static void PublishError(ChatManagerContext context, string message, float lifetimeSeconds = 10)
    {
        OnEvent?.Invoke(new UiEvent
        {
            ChannelCode = $"<color=red>{context.Key}</color>",
            Message = message.Truncate(100),
            LifetimeInSeconds = lifetimeSeconds,
            Timestamp = DateTime.Now
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
