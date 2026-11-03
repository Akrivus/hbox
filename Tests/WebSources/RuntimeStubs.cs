// External boundaries only. Adapters, batching, pitch generation, voting and conversion are production code.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Texture2D { }
    public class SerializeField : Attribute { }
    public class Coroutine { }
    public class WaitUntil { public WaitUntil(Func<bool> predicate) { } }
    public class MonoBehaviour
    {
        public string name = "test";
        public T GetComponentInParent<T>() where T : class => ChatManagerContext.Current as T;
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine();
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { }
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    }
    public static class Random { public static int Range(int min, int max) => min; }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Log(object message) { }
        public static void LogWarning(object message) => Warnings.Add(message.ToString());
        public static void LogError(object message) => Warnings.Add(message.ToString());
        public static void LogException(Exception exception) => Warnings.Add(exception.Message);
    }
}
public class ChatManagerContext
{
    public static ChatManagerContext Current;
    public string Key = Guid.NewGuid().ToString("N");
    public string Name = "test";
    public bool IsActive = true;
    public Actor[] Actors = new[] { new Actor { Name = "France" }, new Actor { Name = "Germany" } };
    public ConfigManager ConfigManager = new ConfigManager();
    public ChatGenerator Generator;
    public T GetComponentInChildren<T>() where T : class => Generator as T;
}
public class Actor { public string Name; }
public static class ChatManager { public static bool IsPaused; }
public class ChatGenerator
{
    public ChatManagerContext ManagerContext;
    public string name = "test";
    public string slug = "test-generator";
    public readonly List<Idea> Ideas = new List<Idea>();
    public void AddIdeaToQueue(Idea idea) => Ideas.Add(idea);
}
public class ConfigManager { public void RegisterConfig(Type type, string key, Action<object> handler) { } }
public static class ServerSource
{
    public static readonly List<(string Slug, Idea Idea)> RoutedIdeas = new List<(string, Idea)>();
    public static bool QueueIdea(string slug, Idea idea) { RoutedIdeas.Add((slug, idea)); return true; }
}
public static class UiEventBus { public static void Publish(ChatManagerContext context, object value) { } }
namespace OpenAI.Chat { }
public class ActorContext { public string Name, Prompt, Context, Memory; }
public class ChatNode
{
    public ActorContext Actor;
    public string Text, OriginalText, Line;
    public bool New;
    public void ReleaseRuntimeAudio() { }
}
public class RecordedPlaybackTimeline { }
public static class TextureExtensions
{
    public static UnityEngine.Texture2D ToTexture2D(this string value) => null;
    public static string ToBase64(this UnityEngine.Texture2D value) => null;
}
public static class MemoryBucket
{
    public static Task<string> GetContext(ChatManagerContext context, string slug, string query) => Task.FromResult("");
}
public class EpisodeRecord { public string slug, title, synopsis, status, channelKey, context; }
public static class OperatorTelemetry
{
    public static IEnumerable<EpisodeRecord> GetRecentEpisodes(int limit) => Array.Empty<EpisodeRecord>();
    public static void RecordEvent(string key, string text, ChatManagerContext context, string channel) { }
}
public enum LlmProfile { Utility, SceneReasoning }
public static class LLM
{
    public static string LastPrompt;
    public static int CandidateCalls;
    public static string PresentationOutput;
    public static object[] LastArgs;
    public static Task<string> AnalyzeImageAsync(ImageAttachment image, Chat chat) => Task.FromResult("Visible source image text and details.");
    public static TaskCompletionSource<bool> Block;
    public static bool Reject;
    public static async Task<string> CompleteAsync(PromptResolver prompt, Chat chat, bool fast)
    {
        CandidateCalls++;
        LastPrompt = prompt.Text;
        if (Block != null) await Block.Task;
        return "Title: The receipt\nPitch: France challenges Germany's probability receipt.\nCast: France, Germany";
    }
    public static Task<string> CompleteAsync(PromptResolver prompt, Chat chat, LlmProfile profile)
    {
        LastArgs = prompt.Args;
        LastPrompt = prompt.Text;
        if (profile == LlmProfile.SceneReasoning) return Task.FromResult(PresentationOutput);
        return Task.FromResult(Reject ? "Verdict: Reject\nReason: Repetitive." : "Verdict: Approve\nReason: Concrete prop.");
    }
}
public class PromptResolver
{
    public static string BasePath = "missing-vault", BaseOutputPath = "Outputs";
    public string Text;
    public object[] Args;
    public PromptResolver(ChatManagerContext context, params string[] paths) { }
    public Task<PromptResolver> Resolve(params object[] args)
    {
        Args = args;
        Text = WebSourcePrompts.Format("{0}", args.Select(value => value?.ToString()).ToArray());
        return Task.FromResult(this);
    }
    public static Task<string> Read(ChatManagerContext context, string path, string blank = null) => Task.FromResult(blank ?? "{0}");
}
public static class TestExtensions
{
    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> values) => values;
}
public class DiscordEmbed { public string Title, Description, URL; public int Color; public DiscordEmbedAuthor Author; public DiscordEmbedThumbnail Thumbnail; }
public class DiscordEmbedAuthor { public string Name, URL, Icon; }
public class DiscordEmbedThumbnail { public string URL; }
public class DiscordWebhookMessage
{
    public string Content;
    public DiscordWebhookMessage(string content, string username, string avatar, params DiscordEmbed[] embeds) { Content = content; }
}
public class DiscordPostedMessage { public string id, channel_id; }
public static class DiscordManager
{
    public static int Posted;
    public static string GetStreamChannel(ChatManagerContext context) => "#stream";
    public static void DeleteWebhookMessageForContext(string context, string channel, string id) { }
    public static void PutInQueueForContext(string context, string channel, DiscordWebhookMessage message, Action<DiscordPostedMessage> callback)
    {
        Posted++;
        callback(new DiscordPostedMessage { id = "message-" + Posted, channel_id = "channel" });
    }
}
public class DiscordBotService
{
    public static void DeleteBotPitchMessage(PitchCandidate candidate) { DeletedSubmissions++; }
    public static int DeletedSubmissions;
    public static DiscordBotService Instance;
    public void PinPitchMessage(DiscordPostedMessage posted, IReadOnlyList<DiscordMessageRef> previous) { }
    public void AddDefaultPitchReactions(DiscordPostedMessage posted) { }
}
