// Only Unity/playback boundaries are stubbed. FolderSource and its JSON manifest
// loading, file filtering, ranking, history and cooldown updates are production code.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace UnityEngine
{
    public class Coroutine { }
    public class MonoBehaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine();
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { }
    }
    public class WaitUntil { public WaitUntil(Func<bool> predicate) { } }
    public class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public static class Application { public static bool isPlaying; }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(object value) { Warnings.Add(value.ToString()); Console.Error.WriteLine(value); }
        public static void LogException(Exception error) => throw error;
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
public static class TaskExtensions
{
    public static IEnumerator AsCoroutine(this Task task) { yield break; }
}
public class ConfigManager
{
    public void RegisterConfig(Type type, string key, Action<object> callback) { }
}
public class ChatManagerContext
{
    public static ChatManagerContext Current;
    public string Key = "pbnn";
    public string Name = "PBNN";
    public ConfigManager ConfigManager = new ConfigManager();
    public event Action OnChatQueueEmpty;
    public event Action<Chat> OnChatQueueAdded;
    public event Action<Chat> OnChatLoaded;
    public void RaiseQueueEmpty() => OnChatQueueEmpty?.Invoke();
    public void RaiseQueued(Chat chat) => OnChatQueueAdded?.Invoke(chat);
    public void RaiseLoaded(Chat chat) => OnChatLoaded?.Invoke(chat);
}
public class ChatManager
{
    public static ChatManager Instance = new ChatManager();
    public bool ReadyForAction = true;
    public ChatManagerContext CurrentContext;
    public readonly List<Chat> Queued = new List<Chat>();
    public void AddToPlayList(Chat chat) => Queued.Add(chat);
}
public class Idea { public string Source; }
public class Chat
{
    public string Key;
    public string FileName;
    public string Title;
    public bool NewEpisode;
    public Idea Idea;
    public static async Task<Chat> Load(string directory, string slug)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), directory, slug + ".json");
        return JsonConvert.DeserializeObject<Chat>(await File.ReadAllTextAsync(path));
    }
    public static void Delete(string directory, string slug) =>
        File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), directory, slug + ".json"));
}
public static class UiEventBus { public static void Publish(ChatManagerContext context, string message) { } }
public class DiscordPostedMessage { public string id; public string channel_id; }
public class EpisodeRecording { }
public class ReplayDiscordBinding
{
    public string channelKey;
    public string slug;
    public string discordMessageId;
    public string discordChannelId;
}

