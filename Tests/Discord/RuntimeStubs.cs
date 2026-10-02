// External boundaries only: production policy, ledger and bot dispatch are compiled above.
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
    public static class Debug { public static void Log(string message) { } public static void LogWarning(string message) { } }
    public static class Application { public static string persistentDataPath; }
    public static class SystemInfo { public static string operatingSystemFamily => "Test"; }
}
namespace Utilities.WebSockets
{
    public enum OpCode { Text }
    public enum State { Open }
    public enum CloseStatusCode { Normal }
    public class DataFrame { public OpCode Type; public string Text; }
    public interface IWebSocket : IDisposable
    {
        State State { get; }
        void Close();
        Task CloseAsync();
        Task SendAsync(string text);
    }
    public class WebSocket : IWebSocket
    {
        public WebSocket(string url) { }
        public State State => State.Open;
        public event Action OnOpen;
        public event Action<DataFrame> OnMessage;
        public event Action<Exception> OnError;
        public event Action<CloseStatusCode, string> OnClose;
        public void Dispose() { }
        public void Close() { }
        public Task CloseAsync() => Task.CompletedTask;
        public Task SendAsync(string text) => Task.CompletedTask;
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
    }
}
public class ChatManagerContext
{
    public static ChatManagerContext Current = new ChatManagerContext();
    public ConfigManager ConfigManager = new ConfigManager();
}
public class ConfigManager { public void RegisterConfig(Type type, string key, Action<IConfig> configure) { } }
public class DiscordPostedMessage { public string id; public string channel_id; }
public class DiscordMessageRef { }
public class ReplayDiscordBinding { public string channelKey = "test"; public string slug = "replay"; }
public class ReplayStatusRecord { public int upVotes; public int downVotes; public int voteScore => upVotes - downVotes; }
public static class FolderSource
{
    public static ReplayStatusRecord Counts = new ReplayStatusRecord();
    public static ReplayDiscordBinding FindReplayByDiscordMessage(string message, string channel) => message == "replay" ? new ReplayDiscordBinding() : null;
    public static ReplayStatusRecord ApplyVote(string channel, string slug, int up, int down, string source, string message)
    { Counts.upVotes += up; Counts.downVotes += down; return Counts; }
}
public static class PitchCandidateStore
{
    public static ReplayStatusRecord Counts = new ReplayStatusRecord();
    public static bool TryApplyVote(string message, string channel, int up, int down, string source)
    { if (message != "pitch") return false; Counts.upVotes += up; Counts.downVotes += down; return true; }
}
public class Channel { public bool active; public string slug; public string name; public string context; }
public static class ServerSource
{
    public static Channel[] GetChannelSnapshot() => Array.Empty<Channel>();
    public static Task<bool> QueueIdea(string slug, string prompt) => Task.FromResult(true);
}
public static class DiscordRateLimit
{
    public static bool TryGetRetryDelay(WebException error, out TimeSpan delay) { delay = default; return false; }
}
