// External runtime boundaries only. ChatGenerator, Chat, Idea and FolderSource are production code.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace UnityEngine
{
    public class Coroutine { }
    public class MonoBehaviour
    {
        public string name = "Default";
        public object Parent;
        public T GetComponentInParent<T>() where T : class => Parent as T;
        public T[] GetComponents<T>() => Array.Empty<T>();
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine();
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { }
    }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class Texture2D { }
    public class WaitUntil { public WaitUntil(Func<bool> predicate) { } }
    public class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public static class Application { public static bool isPlaying; }
    public static class Debug
    {
        public static void LogError(object value) { }
        public static void LogWarning(object value) { }
        public static void LogException(Exception error) { }
    }
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Clamp(int n, int min, int max) => Math.Clamp(n, min, max);
        public static float Clamp(float n, float min, float max) => Math.Clamp(n, min, max);
    }
}
namespace OpenAI.Chat { }
public class WaitUntilTimer { public WaitUntilTimer(Func<bool> f, int n) { } }
public static class Extensions
{
    public static IEnumerator AsCoroutine(this Task task) { yield break; }
    public static string Chomp(this string s) => s;
    public static T[] Shuffle<T>(this T[] items) => items;
    public static string Find(this string text, string key) => null;
    public static UnityEngine.Texture2D ToTexture2D(this string s) => null;
    public static string ToBase64(this UnityEngine.Texture2D t) => null;
}
public class ConfigManager { public void RegisterConfig(Type type, string key, Action<object> callback) { } }
public class ChatManagerContext
{
    public static ChatManagerContext Current;
    public string Key = "pbnn", Name;
    public bool IsActive = true;
    public string[] SharedMemoryBuckets = Array.Empty<string>();
    public Actor[] Actors = Array.Empty<Actor>();
    public string[] Locations = Array.Empty<string>();
    public Dictionary<string,Actor> ActorsSearch = new Dictionary<string,Actor>();
    public ConfigManager ConfigManager = new ConfigManager();
    public event Action OnChatQueueEmpty;
    public event Action<Chat> OnChatQueueAdded;
    public event Action<Chat> OnChatLoaded;
}
public class Actor { public string Name, Pronouns; }
public class ActorContext
{
    public string Name, Prompt, Context, Memory;
    public ActorContext() { }
    public ActorContext(Actor a) { Name = a.Name; }
}
public class ChatNode
{
    public ActorContext Actor;
    public string Text, OriginalText;
    public bool New;
    public void ReleaseRuntimeAudio() { }
}
public class ChatManager
{
    public static ChatManager Instance = new ChatManager();
    public bool ReadyForAction = true;
    public ChatManagerContext CurrentContext;
    public readonly List<Chat> Queued = new List<Chat>();
    public void AddToPlayList(Chat chat) => Queued.Add(chat);
}
public interface ISubGenerator { bool IsBlocking { get; } Task Generate(PromptResolver prompt, Chat chat); }
public class PromptResolver
{
    public static bool FailSave;
    public PromptResolver(ChatManagerContext c, string n, string b) { }
    public PromptResolver(ChatGenerator g) { }
    public PromptResolver(ChatGenerator g, ISubGenerator s) { }
    public Task SaveOutput(string text) => FailSave ? Task.FromException(new Exception("prompt write failure")) : Task.CompletedTask;
    public Task<PromptResolver> Resolve(params string[] args) => Task.FromResult(this);
}
public class MemoryBucket
{
    public static Task<string> GetContext(ChatManagerContext c, string s, string q) => Task.FromResult("");
    public static Task<MemoryBucket> Get(ChatManagerContext c, string s) => Task.FromResult(new MemoryBucket());
    public Task<string> GetRelevant(string q) => Task.FromResult("");
}
public static class LLM
{
    public static Exception Failure;
    public static TaskCompletionSource<bool> Gate;
    public static int Calls;
    public static async Task<string> CompleteAsync(PromptResolver p, Chat c, bool fast)
    {
        Calls++;
        if (Gate != null) await Gate.Task;
        if (Failure != null) throw Failure;
        c.Title = "Test Bulletin";
        return "fixture";
    }
}
public static class OperatorTelemetry
{
    public static int Completed, Failed;
    public static void RecordIdeaReceived(ChatGenerator g, string p) { }
    public static void RecordGenerationStarted(ChatGenerator g, Idea i) { }
    public static void RecordGenerationFailed(ChatGenerator g, Idea i, Exception e) => Failed++;
    public static void RecordGenerationCompleted(Chat c) => Completed++;
}
public class ServerSource
{
    public static ServerSource Instance;
    public void RegisterGenerator(ChatGenerator g) { }
    public void UnregisterGenerator(ChatGenerator g) { }
}
public static class UiEventBus { public static void Publish(ChatManagerContext c, string s) { } }
public class DiscordPostedMessage { public string id, channel_id; }
public class EpisodeRecording { }
public class ReplayDiscordBinding { public string channelKey, slug, discordMessageId, discordChannelId; }
