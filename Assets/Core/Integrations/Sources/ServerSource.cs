using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class ServerSource : MonoBehaviour
{
    public static ServerSource Instance { get; private set; }
    private static readonly object routeLock = new object();

    private static readonly Dictionary<string, Dictionary<string, Func<HttpListenerContext, Task>>> routes =
        new Dictionary<string, Dictionary<string, Func<HttpListenerContext, Task>>>()
        {
            { "GET", new Dictionary<string, Func<HttpListenerContext, Task>>() },
            { "POST", new Dictionary<string, Func<HttpListenerContext, Task>>() },
            { "PUT", new Dictionary<string, Func<HttpListenerContext, Task>>() },
            { "PATCH", new Dictionary<string, Func<HttpListenerContext, Task>>() },
            { "DELETE", new Dictionary<string, Func<HttpListenerContext, Task>>() },
        };

    private readonly object generatorLock = new object();
    private readonly Dictionary<string, GeneratorRuntimeInfo> generators = new Dictionary<string, GeneratorRuntimeInfo>(StringComparer.OrdinalIgnoreCase);

    private HttpListener listener;
    private Thread thread;
    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private CancellationToken token;

    public bool IsListening { get; private set; } = true;

    private void Awake()
    {
        if (Instance != null)
            Debug.LogWarning("Multiple ServerIntegrations found, this is not good.");
        Instance = this;

        AddRoute("GET", "/", context => ProcessFileRequest(context, "index.html"));
        AddApiRoute("GET", "/api/health", GetHealthAsync);
        AddApiRoute("GET", "/api/channels", GetChannelsAsync);
        AddRoute("GET", "/api/events", GetEventsAsync);
        AddRoute("GET", "/api/episodes/recent", GetRecentEpisodesAsync);
        AddRoute("GET", "/api/replays", GetReplayStatusAsync);
    }

    private void Start()
    {
        token = cts.Token;
        listener = new HttpListener();
        listener.Prefixes.Add($"http://{GetLocalIPAddress()}:6789/");
        listener.Prefixes.Add("http://localhost:6789/");
        thread = new Thread(() => Listen().GetAwaiter().GetResult());
        thread.Start();
    }

    private void OnDestroy()
    {
        IsListening = false;
        cts.Cancel();

        if (listener != null)
        {
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }

        StopAllCoroutines();
    }

    private async Task Listen()
    {
        listener.Start();

        try
        {
            while (!token.IsCancellationRequested && listener.IsListening && IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }

                await ProcessRequest(context);
            }
        }
        finally
        {
            if (listener != null && listener.IsListening)
                listener.Close();
        }
    }

    private async Task ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.KeepAlive = false;
        response.StatusCode = 200;

        var method = request.HttpMethod;
        var path = request.Url.AbsolutePath;

        try
        {
            Func<HttpListenerContext, Task> handler = null;
            var methodExists = false;

            lock (routeLock)
            {
                methodExists = routes.TryGetValue(method, out var methodRoutes);
                if (methodExists)
                    methodRoutes.TryGetValue(path, out handler);
            }

            if (!methodExists)
            {
                response.StatusCode = 405;
                await WriteJsonAsync(response, new ApiErrorResponse("method_not_allowed", $"Unsupported method '{method}'."));
                return;
            }

            if (handler == null)
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new ApiErrorResponse("not_found", $"No route registered for '{path}'."));
                return;
            }

            await handler(context);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (response.OutputStream.CanWrite)
            {
                response.StatusCode = 500;
                await WriteJsonAsync(response, new ApiErrorResponse("server_error", e.Message));
            }
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }
    }

    public void RegisterGenerator(ChatGenerator generator)
    {
        if (generator == null || generator.ManagerContext == null)
            return;

        var info = new GeneratorRuntimeInfo(generator);

        lock (generatorLock)
            generators[info.slug] = info;

        AddRoute("POST", $"/api/channels/{info.slug}/ideas", context => ProcessBodyString(context, body =>
        {
            generator.AddPromptToQueue(body);
            return Task.CompletedTask;
        }));
    }

    public void UnregisterGenerator(ChatGenerator generator)
    {
        if (generator == null)
            return;

        lock (generatorLock)
            generators.Remove(generator.slug);

        RemoveRoute("POST", $"/api/channels/{generator.slug}/ideas");
    }

    private Task<HealthResponse> GetHealthAsync()
    {
        return Task.FromResult(new HealthResponse
        {
            status = IsListening && listener != null && listener.IsListening ? "ok" : "starting",
            listening = IsListening && listener != null && listener.IsListening,
            address = "http://localhost:6789"
        });
    }

    private Task<IReadOnlyList<GeneratorRuntimeInfo>> GetChannelsAsync()
    {
        lock (generatorLock)
        {
            var snapshot = generators.Values
                .OrderBy(g => g.context)
                .ThenBy(g => g.name)
                .Select(g => g.WithRuntime())
                .ToList();
            return Task.FromResult((IReadOnlyList<GeneratorRuntimeInfo>)snapshot);
        }
    }

    private Task GetEventsAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 50);
        return WriteJsonAsync(context.Response, OperatorTelemetry.GetRecentEvents(limit));
    }

    private Task GetRecentEpisodesAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 30);
        return WriteJsonAsync(context.Response, OperatorTelemetry.GetRecentEpisodes(limit));
    }

    private Task GetReplayStatusAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 50);
        query.TryGetValue("channelKey", out var channelKey);
        return WriteJsonAsync(context.Response, FolderSource.GetReplayStatus(channelKey, limit));
    }

    public static Task ProcessFileRequest(HttpListenerContext context, string path)
    {
        var file = Path.Combine(Application.streamingAssetsPath, path);
        var text = File.ReadAllText(file);
        return context.Response.WriteStringAsync(text, "text/html; charset=utf-8");
    }

    public static async Task ProcessBodyString(HttpListenerContext context, Func<string, Task> handler)
    {
        var text = await ReadBodyAsStringAsync(context.Request);
        await handler(text);
        await context.Response.WriteStringAsync("OK", "text/plain; charset=utf-8");
    }

    public static void Register(string method, string path, Func<HttpListenerContext, Task> handler)
    {
        lock (routeLock)
        {
            if (!routes.ContainsKey(method))
                throw new ArgumentException("Invalid method: " + method);
            routes[method][path] = handler;
        }
    }

    public static void RemoveRoute(string method, string path)
    {
        lock (routeLock)
        {
            if (!routes.TryGetValue(method, out var methodRoutes))
                return;
            methodRoutes.Remove(path);
        }
    }

    public static void AddRoute(string method, string path, Func<HttpListenerContext, Task> handler)
    {
        Register(method, path, handler);
    }

    public static void AddRoute(string method, string path, Action<HttpListenerContext> handler)
    {
        Register(method, path, context =>
        {
            handler(context);
            return Task.CompletedTask;
        });
    }

    public static void AddRoute(string method, string path, Func<string, Task<string>> handler)
    {
        Register(method, path, async context => await Route(context, handler));
    }

    public static void AddApiRoute<I, O>(string method, string path, Func<I, Task<O>> handler)
    {
        Register(method, path, async context => await ApiRoute(context, handler));
    }

    public static void AddApiRoute<O>(string method, string path, Func<Task<O>> handler)
    {
        Register(method, path, async context => await ApiRoute(context, handler));
    }

    public static void AddGetRoute(string path, Action<Dictionary<string, string>, HttpListenerResponse> handler)
    {
        AddRoute("GET", path, context =>
        {
            GetRoute(context, handler);
            return Task.CompletedTask;
        });
    }

    public static async Task Route(HttpListenerContext context, Func<string, Task<string>> route, string contentType = "text/plain; charset=utf-8")
    {
        var text = await ReadBodyAsStringAsync(context.Request);
        await context.Response.WriteStringAsync(await route(text), contentType);
    }

    public static async Task ApiRoute<I, O>(HttpListenerContext context, Func<I, Task<O>> route)
    {
        var text = await ReadBodyAsStringAsync(context.Request);
        var input = string.IsNullOrWhiteSpace(text) ? default : JsonConvert.DeserializeObject<I>(text);
        var output = await route(input);
        await WriteJsonAsync(context.Response, output);
    }

    public static async Task ApiRoute<O>(HttpListenerContext context, Func<Task<O>> route)
    {
        var output = await route();
        await WriteJsonAsync(context.Response, output);
    }

    public static void GetRoute(HttpListenerContext context, Action<Dictionary<string, string>, HttpListenerResponse> route)
    {
        var dict = ParseQueryString(context.Request.Url.Query);
        route(dict, context.Response);
    }

    public static async Task<string> ReadBodyAsStringAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
            return string.Empty;

        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            return await reader.ReadToEndAsync();
    }

    public static Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        var json = JsonConvert.SerializeObject(payload);
        return response.WriteStringAsync(json, "application/json; charset=utf-8");
    }

    public static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return dict;

        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return dict;

        foreach (var segment in trimmed.Split('&'))
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var parts = segment.Split(new[] { '=' }, 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            dict[key] = value;
        }

        return dict;
    }

    public static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        throw new Exception("No network adapters with an IPv4 address in the system!");
    }

    private static int ParseLimit(Dictionary<string, string> query, int fallback)
    {
        if (query != null && query.TryGetValue("limit", out var raw) && int.TryParse(raw, out var limit) && limit > 0)
            return limit;
        return fallback;
    }

    [Serializable]
    private sealed class ApiErrorResponse
    {
        public ApiErrorResponse(string code, string message)
        {
            this.code = code;
            this.message = message;
        }

        public string code;
        public string message;
    }

    [Serializable]
    private sealed class HealthResponse
    {
        public string status;
        public bool listening;
        public string address;
    }
}

[Serializable]
public sealed class GeneratorRuntimeInfo
{
    [NonSerialized] private ChatGenerator generator;

    public string context;
    public string channelKey;
    public string name;
    public string slug;
    public string ideasHref;
    public bool active;
    public bool isGenerating;
    public int queueDepth;

    public GeneratorRuntimeInfo()
    {
    }

    public GeneratorRuntimeInfo(ChatGenerator generator)
    {
        this.generator = generator;
        var managerContext = generator.ManagerContext;

        context = managerContext?.Name;
        channelKey = managerContext?.Key;
        name = generator.name;
        slug = generator.slug;
        ideasHref = $"/api/channels/{slug}/ideas";
    }

    public GeneratorRuntimeInfo WithRuntime()
    {
        if (generator == null)
            return this;

        active = generator.ManagerContext != null && generator.ManagerContext.IsActive;
        isGenerating = generator.IsGenerating;
        queueDepth = generator.QueueDepth;
        return this;
    }
}

public static class OperatorTelemetry
{
    private static readonly object sync = new object();
    private static readonly List<OperatorEventRecord> events = new List<OperatorEventRecord>();
    private static readonly Dictionary<string, EpisodeRecord> episodes = new Dictionary<string, EpisodeRecord>(StringComparer.OrdinalIgnoreCase);

    private const int MaxEvents = 300;
    private const int MaxEpisodes = 150;

    public static void RecordEvent(string type, string message, ChatManagerContext context = null, string channelKey = null, string episodeSlug = null)
    {
        if (string.IsNullOrWhiteSpace(type))
            type = "info";

        var record = new OperatorEventRecord
        {
            type = type,
            message = message ?? string.Empty,
            channelKey = channelKey ?? context?.Key ?? string.Empty,
            context = context?.Name ?? string.Empty,
            episodeSlug = episodeSlug ?? string.Empty,
            timestamp = DateTimeOffset.Now.ToString("O")
        };

        lock (sync)
        {
            events.Add(record);
            TrimEventsUnsafe();
        }
    }

    public static void RecordIdeaReceived(ChatGenerator generator, string prompt)
    {
        if (generator == null)
            return;

        RecordEvent(
            "idea_received",
            string.IsNullOrWhiteSpace(prompt) ? "Idea received." : $"Idea received: {prompt}",
            generator.ManagerContext,
            generator.ManagerContext?.Key);
    }

    public static void RecordGenerationStarted(ChatGenerator generator, Idea idea)
    {
        if (generator == null || idea == null)
            return;

        RecordEvent(
            "generation_started",
            string.IsNullOrWhiteSpace(idea.Prompt) ? "Generation started." : $"Generation started: {idea.Prompt}",
            generator.ManagerContext,
            generator.ManagerContext?.Key,
            idea.Slug);

        lock (sync)
        {
            UpsertEpisodeUnsafe(new EpisodeRecord
            {
                slug = idea.Slug,
                title = string.Empty,
                channelKey = generator.ManagerContext?.Key ?? string.Empty,
                context = generator.ManagerContext?.Name ?? string.Empty,
                source = idea.Source ?? string.Empty,
                author = idea.Author ?? string.Empty,
                prompt = idea.Prompt ?? string.Empty,
                generatedAt = string.Empty,
                queuedAt = string.Empty,
                playedAt = string.Empty,
                status = "generating"
            });
        }
    }

    public static void RecordGenerationCompleted(Chat chat)
    {
        if (chat == null)
            return;

        var title = string.IsNullOrWhiteSpace(chat.Title) ? chat.FileName : chat.Title;
        RecordEvent(
            "generation_completed",
            $"Generated: {title}",
            chat.ManagerContext,
            chat.ManagerContext?.Key ?? chat.Key,
            chat.FileName);

        lock (sync)
        {
            UpsertEpisodeUnsafe(new EpisodeRecord
            {
                slug = chat.FileName ?? string.Empty,
                title = title ?? string.Empty,
                channelKey = chat.ManagerContext?.Key ?? chat.Key ?? string.Empty,
                context = chat.ManagerContext?.Name ?? string.Empty,
                source = chat.Idea?.Source ?? string.Empty,
                author = chat.Idea?.Author ?? string.Empty,
                prompt = chat.Idea?.Prompt ?? string.Empty,
                generatedAt = DateTimeOffset.Now.ToString("O"),
                queuedAt = string.Empty,
                playedAt = string.Empty,
                status = "generated"
            });
        }
    }

    public static void RecordGenerationFailed(ChatGenerator generator, Idea idea, Exception exception)
    {
        if (generator == null)
            return;

        RecordEvent(
            "generation_failed",
            exception == null ? "Generation failed." : $"Generation failed: {exception.Message}",
            generator.ManagerContext,
            generator.ManagerContext?.Key,
            idea?.Slug);

        lock (sync)
        {
            if (idea != null)
            {
                UpsertEpisodeUnsafe(new EpisodeRecord
                {
                    slug = idea.Slug,
                    title = string.Empty,
                    channelKey = generator.ManagerContext?.Key ?? string.Empty,
                    context = generator.ManagerContext?.Name ?? string.Empty,
                    source = idea.Source ?? string.Empty,
                    author = idea.Author ?? string.Empty,
                    prompt = idea.Prompt ?? string.Empty,
                    generatedAt = string.Empty,
                    queuedAt = string.Empty,
                    playedAt = string.Empty,
                    status = "failed"
                });
            }
        }
    }

    public static void RecordEpisodeQueued(Chat chat)
    {
        if (chat == null)
            return;

        RecordEvent(
            "episode_queued",
            $"Queued: {chat.Title ?? chat.FileName}",
            chat.ManagerContext,
            chat.ManagerContext?.Key ?? chat.Key,
            chat.FileName);

        lock (sync)
        {
            UpsertEpisodeUnsafe(new EpisodeRecord
            {
                slug = chat.FileName ?? string.Empty,
                title = chat.Title ?? chat.FileName ?? string.Empty,
                channelKey = chat.ManagerContext?.Key ?? chat.Key ?? string.Empty,
                context = chat.ManagerContext?.Name ?? string.Empty,
                source = chat.Idea?.Source ?? string.Empty,
                author = chat.Idea?.Author ?? string.Empty,
                prompt = chat.Idea?.Prompt ?? string.Empty,
                generatedAt = string.Empty,
                queuedAt = DateTimeOffset.Now.ToString("O"),
                playedAt = string.Empty,
                status = "queued"
            });
        }
    }

    public static void RecordEpisodePlaying(Chat chat)
    {
        if (chat == null)
            return;

        RecordEvent(
            "episode_playing",
            $"Now playing: {chat.Title ?? chat.FileName}",
            chat.ManagerContext,
            chat.ManagerContext?.Key ?? chat.Key,
            chat.FileName);

        lock (sync)
        {
            UpsertEpisodeUnsafe(new EpisodeRecord
            {
                slug = chat.FileName ?? string.Empty,
                title = chat.Title ?? chat.FileName ?? string.Empty,
                channelKey = chat.ManagerContext?.Key ?? chat.Key ?? string.Empty,
                context = chat.ManagerContext?.Name ?? string.Empty,
                source = chat.Idea?.Source ?? string.Empty,
                author = chat.Idea?.Author ?? string.Empty,
                prompt = chat.Idea?.Prompt ?? string.Empty,
                generatedAt = string.Empty,
                queuedAt = string.Empty,
                playedAt = DateTimeOffset.Now.ToString("O"),
                status = "playing"
            });
        }
    }

    public static IReadOnlyList<OperatorEventRecord> GetRecentEvents(int limit = 50)
    {
        lock (sync)
        {
            return events
                .OrderByDescending(e => e.timestamp)
                .Take(NormalizeLimit(limit, 200))
                .ToList();
        }
    }

    public static IReadOnlyList<EpisodeRecord> GetRecentEpisodes(int limit = 50)
    {
        lock (sync)
        {
            return episodes.Values
                .OrderByDescending(e => FirstNonEmptyTimestamp(e.playedAt, e.queuedAt, e.generatedAt))
                .Take(NormalizeLimit(limit, 200))
                .Select(CloneEpisode)
                .ToList();
        }
    }

    private static void UpsertEpisodeUnsafe(EpisodeRecord incoming)
    {
        if (incoming == null || string.IsNullOrWhiteSpace(incoming.slug))
            return;

        if (!episodes.TryGetValue(incoming.slug, out var existing))
        {
            episodes[incoming.slug] = CloneEpisode(incoming);
            TrimEpisodesUnsafe();
            return;
        }

        existing.title = Prefer(incoming.title, existing.title);
        existing.channelKey = Prefer(incoming.channelKey, existing.channelKey);
        existing.context = Prefer(incoming.context, existing.context);
        existing.source = Prefer(incoming.source, existing.source);
        existing.author = Prefer(incoming.author, existing.author);
        existing.prompt = Prefer(incoming.prompt, existing.prompt);
        existing.generatedAt = Prefer(incoming.generatedAt, existing.generatedAt);
        existing.queuedAt = Prefer(incoming.queuedAt, existing.queuedAt);
        existing.playedAt = Prefer(incoming.playedAt, existing.playedAt);
        existing.status = Prefer(incoming.status, existing.status);
    }

    private static void TrimEventsUnsafe()
    {
        if (events.Count <= MaxEvents)
            return;
        events.RemoveRange(0, events.Count - MaxEvents);
    }

    private static void TrimEpisodesUnsafe()
    {
        if (episodes.Count <= MaxEpisodes)
            return;

        var keep = episodes.Values
            .OrderByDescending(e => FirstNonEmptyTimestamp(e.playedAt, e.queuedAt, e.generatedAt))
            .Take(MaxEpisodes)
            .Select(e => e.slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remove = episodes.Keys.Where(k => !keep.Contains(k)).ToList();
        foreach (var key in remove)
            episodes.Remove(key);
    }

    private static int NormalizeLimit(int requested, int max)
    {
        if (requested < 1)
            return 50;
        return Math.Min(requested, max);
    }

    private static string Prefer(string incoming, string existing)
    {
        return string.IsNullOrWhiteSpace(incoming) ? existing ?? string.Empty : incoming;
    }

    private static string FirstNonEmptyTimestamp(params string[] timestamps)
    {
        return timestamps.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty;
    }

    private static EpisodeRecord CloneEpisode(EpisodeRecord episode)
    {
        return new EpisodeRecord
        {
            slug = episode.slug,
            title = episode.title,
            channelKey = episode.channelKey,
            context = episode.context,
            source = episode.source,
            author = episode.author,
            prompt = episode.prompt,
            generatedAt = episode.generatedAt,
            queuedAt = episode.queuedAt,
            playedAt = episode.playedAt,
            status = episode.status
        };
    }
}

[Serializable]
public sealed class OperatorEventRecord
{
    public string timestamp;
    public string type;
    public string message;
    public string channelKey;
    public string context;
    public string episodeSlug;
}

[Serializable]
public sealed class EpisodeRecord
{
    public string slug;
    public string title;
    public string channelKey;
    public string context;
    public string source;
    public string author;
    public string prompt;
    public string generatedAt;
    public string queuedAt;
    public string playedAt;
    public string status;
}
