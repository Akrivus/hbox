using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticsProcess = System.Diagnostics.Process;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    private readonly object pendingIdeaLock = new object();
    private readonly Queue<PendingIdeaRequest> pendingIdeaRequests = new Queue<PendingIdeaRequest>();
    private IReadOnlyList<GeneratorRuntimeInfo> cachedGeneratorSnapshot = Array.Empty<GeneratorRuntimeInfo>();

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
        AddApiRoute("GET", "/api/diagnostics/memory", GetMemoryDiagnosticsAsync);
        AddRoute("GET", "/api/diagnostics/memory/history", GetMemoryDiagnosticsHistoryAsync);
        AddRoute("GET", "/api/events", GetEventsAsync);
        AddRoute("GET", "/api/llm/calls", GetLlmCallsAsync);
        AddRoute("GET", "/api/llm/summary", GetLlmSummaryAsync);
        AddRoute("GET", "/api/llm/budget", GetLlmBudgetAsync);
        AddRoute("GET", "/api/llm/usage", GetLlmUsageAsync);
        AddRoute("GET", "/api/llm/usage/calls", GetLlmUsageCallsAsync);
        AddRoute("GET", "/api/llm/history/calls", GetLlmHistoryCallsAsync);
        AddRoute("GET", "/api/llm/history/budget", GetLlmHistoryBudgetAsync);
        AddRoute("GET", "/api/episodes/recent", GetRecentEpisodesAsync);
        AddRoute("GET", "/api/pitches", GetPitchStatusAsync);
        AddRoute("GET", "/api/replays", GetReplayStatusAsync);
        AddRoute("GET", "/vault", GetVaultPathAsync);
        AddApiRoute<PitchVoteRequest, PitchCandidate>("POST", "/api/pitches/vote", VoteOnPitchAsync);
        AddApiRoute<ReplayVoteRequest, ReplayStatusRecord>("POST", "/api/replays/vote", VoteOnReplayAsync);
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

    private void Update()
    {
        ProcessPendingIdeaRequests();
        RefreshGeneratorSnapshot();
        OperatorTelemetry.CaptureRequestedMemorySnapshot();
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
                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && IsVaultPath(path))
                {
                    await GetVaultPathAsync(context);
                    return;
                }

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
            EnqueueIdeaRequest(info.slug, body);
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

    public static IReadOnlyList<GeneratorRuntimeInfo> GetChannelSnapshot()
    {
        if (Instance == null)
            return Array.Empty<GeneratorRuntimeInfo>();

        lock (Instance.generatorLock)
        {
            return Instance.cachedGeneratorSnapshot.ToList();
        }
    }

    public static bool QueueIdea(string slug, string prompt)
    {
        if (Instance == null || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(prompt))
            return false;

        lock (Instance.generatorLock)
        {
            if (!Instance.generators.ContainsKey(slug))
                return false;
        }

        Instance.EnqueueIdeaRequest(slug, prompt);
        return true;
    }

    private void EnqueueIdeaRequest(string slug, string prompt)
    {
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(prompt))
            return;

        lock (pendingIdeaLock)
            pendingIdeaRequests.Enqueue(new PendingIdeaRequest(slug, prompt));
    }

    private void ProcessPendingIdeaRequests()
    {
        while (true)
        {
            PendingIdeaRequest request;
            lock (pendingIdeaLock)
            {
                if (pendingIdeaRequests.Count == 0)
                    return;

                request = pendingIdeaRequests.Dequeue();
            }

            ChatGenerator generator = null;
            lock (generatorLock)
                if (generators.TryGetValue(request.slug, out var info))
                    generator = info.generator;

            generator?.AddPromptToQueue(request.prompt);
        }
    }

    private void RefreshGeneratorSnapshot()
    {
        lock (generatorLock)
        {
            cachedGeneratorSnapshot = generators.Values
                .OrderBy(g => g.context)
                .ThenBy(g => g.name)
                .Select(g => g.WithRuntime())
                .ToList();
        }
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
            return Task.FromResult(cachedGeneratorSnapshot);
        }
    }

    private Task<MemoryDiagnosticsSnapshot> GetMemoryDiagnosticsAsync()
    {
        OperatorTelemetry.RequestMemorySnapshot("operator_poll");
        return Task.FromResult(OperatorTelemetry.GetLatestMemorySnapshot());
    }

    private Task GetMemoryDiagnosticsHistoryAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 20);
        return WriteJsonAsync(context.Response, OperatorTelemetry.GetRecentMemorySnapshots(limit));
    }

    private Task GetEventsAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 50);
        return WriteJsonAsync(context.Response, OperatorTelemetry.GetRecentEvents(limit));
    }

    private Task GetLlmCallsAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 100);
        query.TryGetValue("promptPart", out var promptPart);
        query.TryGetValue("profile", out var profile);
        query.TryGetValue("model", out var model);
        query.TryGetValue("callerType", out var callerType);
        query.TryGetValue("channelKey", out var channelKey);

        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetRecent(limit, promptPart, profile, model, callerType, channelKey));
    }

    private Task GetLlmSummaryAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 1000);
        query.TryGetValue("groupBy", out var groupBy);
        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetBreakdown(groupBy, limit));
    }

    private Task GetLlmBudgetAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 1000);
        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetBudgetBreakdown(limit));
    }

    private Task GetLlmUsageAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 5000);
        query.TryGetValue("range", out var range);
        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetUsageBreakdown(range, limit));
    }

    private Task GetLlmUsageCallsAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 1000);
        query.TryGetValue("range", out var range);
        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetUsageCalls(range, limit));
    }

    private Task GetLlmHistoryCallsAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 1000);
        var date = ParseDate(query);
        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetPersistedCalls(date, limit));
    }

    private Task GetLlmHistoryBudgetAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 5000);
        var date = ParseDate(query);
        return WriteJsonAsync(context.Response, LlmCallTelemetry.GetPersistedBudgetBreakdown(date, limit));
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

    private Task GetPitchStatusAsync(HttpListenerContext context)
    {
        var query = ParseQueryString(context.Request.Url.Query);
        var limit = ParseLimit(query, 50);
        query.TryGetValue("channelKey", out var channelKey);
        return WriteJsonAsync(context.Response, PitchCandidateStore.GetPitchStatus(channelKey, limit));
    }

    private Task<PitchCandidate> VoteOnPitchAsync(PitchVoteRequest request)
    {
        if (request == null)
            throw new ArgumentException("Pitch vote payload is required.");
        if (string.IsNullOrWhiteSpace(request.channelKey))
            throw new ArgumentException("Pitch vote requires a channelKey.");
        if (string.IsNullOrWhiteSpace(request.id))
            throw new ArgumentException("Pitch vote requires an id.");

        if (!TryResolveVoteDeltas(request.delta, request.upDelta, request.downDelta, out var upDelta, out var downDelta))
            throw new ArgumentException("Pitch vote requires a non-zero upDelta, downDelta, or delta.");

        var updated = PitchCandidateStore.ApplyVote(request.channelKey, request.id, upDelta, downDelta, request.source);
        if (updated == null)
            throw new ArgumentException($"Pitch '{request.id}' was not found for channel '{request.channelKey}'.");

        return Task.FromResult(updated);
    }

    private Task<ReplayStatusRecord> VoteOnReplayAsync(ReplayVoteRequest request)
    {
        if (request == null)
            throw new ArgumentException("Replay vote payload is required.");
        if (string.IsNullOrWhiteSpace(request.channelKey))
            throw new ArgumentException("Replay vote requires a channelKey.");
        if (string.IsNullOrWhiteSpace(request.slug))
            throw new ArgumentException("Replay vote requires a slug.");

        if (!TryResolveVoteDeltas(request.delta, request.upDelta, request.downDelta, out var upDelta, out var downDelta))
            throw new ArgumentException("Replay vote requires a non-zero upDelta, downDelta, or delta.");

        var updated = FolderSource.ApplyVote(request.channelKey, request.slug, upDelta, downDelta, request.source, request.messageId);
        if (updated == null)
            throw new ArgumentException($"Replay '{request.slug}' was not found for channel '{request.channelKey}'.");

        return Task.FromResult(updated);
    }

    private static bool TryResolveVoteDeltas(int delta, int requestedUpDelta, int requestedDownDelta, out int upDelta, out int downDelta)
    {
        upDelta = requestedUpDelta;
        downDelta = requestedDownDelta;

        if (upDelta == 0 && downDelta == 0 && delta != 0)
        {
            upDelta = delta > 0 ? delta : 0;
            downDelta = delta < 0 ? -delta : 0;
        }

        return upDelta != 0 || downDelta != 0;
    }

    public static Task ProcessFileRequest(HttpListenerContext context, string path)
    {
        var file = Path.Combine(Application.streamingAssetsPath, path);
        var text = File.ReadAllText(file);
        return context.Response.WriteStringAsync(text, "text/html; charset=utf-8");
    }

    private static async Task GetVaultPathAsync(HttpListenerContext context)
    {
        var requestPath = context.Request.Url.AbsolutePath;
        if (!TryResolveVaultRequestPath(requestPath, out var path, out var relativePath, out var isDirectory, out var errorCode, out var errorMessage))
        {
            if (errorCode == 404 && TryGetVaultParentUrl(requestPath, out var parentUrl))
            {
                context.Response.Redirect(parentUrl);
                return;
            }

            context.Response.StatusCode = errorCode;
            await context.Response.WriteStringAsync(RenderVaultErrorPage(errorMessage), "text/html; charset=utf-8");
            return;
        }

        if (isDirectory)
        {
            await context.Response.WriteStringAsync(RenderVaultDirectoryPage(path, relativePath), "text/html; charset=utf-8");
            return;
        }

        var query = ParseQueryString(context.Request.Url.Query);
        var wantsRaw = query.TryGetValue("raw", out var raw) &&
            (string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase));

        if (IsMarkdownFile(path) && !wantsRaw)
        {
            var markdown = await Task.Run(() => File.ReadAllText(path));
            await context.Response.WriteStringAsync(RenderVaultMarkdownPage(markdown, path, relativePath), "text/html; charset=utf-8");
            return;
        }

        var bytes = await Task.Run(() => File.ReadAllBytes(path));
        context.Response.ContentType = GetContentType(path);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    public static string GetVaultUrl(string path)
    {
        if (!TryGetVaultRelativePath(path, out var relative))
            return string.Empty;

        return "/vault/" + string.Join("/", relative
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
    }

    private static bool IsVaultPath(string path)
    {
        return path != null &&
            (string.Equals(path, "/vault", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/vault/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveVaultRequestPath(string requestPath, out string path, out string relativePath, out bool isDirectory, out int errorCode, out string errorMessage)
    {
        path = string.Empty;
        relativePath = string.Empty;
        isDirectory = false;
        errorCode = 404;
        errorMessage = "Vault path not found.";

        if (!IsVaultPath(requestPath))
            return false;

        var relative = string.Equals(requestPath, "/vault", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Uri.UnescapeDataString(requestPath.Substring("/vault/".Length)).Replace('/', Path.DirectorySeparatorChar);

        var root = GetVaultRoot();
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsSameOrUnderDirectory(fullPath, root))
        {
            errorCode = 403;
            errorMessage = "Requested path is outside the Vault.";
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            path = fullPath;
            relativePath = GetVaultRelativePath(fullPath);
            isDirectory = true;
            return true;
        }

        if (!File.Exists(fullPath))
            return false;

        path = fullPath;
        relativePath = GetVaultRelativePath(fullPath);
        return true;
    }

    private static string GetVaultRelativePath(string path)
    {
        var root = GetVaultRoot();
        var fullPath = Path.GetFullPath(path);
        if (!IsSameOrUnderDirectory(fullPath, root))
            return string.Empty;

        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetVaultParentRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        var parent = Path.GetDirectoryName(relativePath);
        return string.IsNullOrWhiteSpace(parent) ? string.Empty : parent;
    }

    private static bool TryGetVaultParentUrl(string requestPath, out string parentUrl)
    {
        parentUrl = "/vault";
        if (!IsVaultPath(requestPath) || string.Equals(requestPath, "/vault", StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = Uri.UnescapeDataString(requestPath.Substring("/vault/".Length)).Replace('/', Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(relative);
        parentUrl = string.IsNullOrWhiteSpace(parent) ? "/vault" : BuildVaultUrl(parent);
        return true;
    }

    private static bool IsHiddenVaultEntry(string name)
    {
        return name.StartsWith(".", StringComparison.Ordinal);
    }

    private static bool IsMarkdownFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderVaultDirectoryPage(string directory, string relativePath)
    {
        var title = string.IsNullOrWhiteSpace(relativePath) ? "Vault" : relativePath.Replace('\\', '/');
        var parentPath = GetVaultParentRelativePath(relativePath);
        var entries = Directory.GetFileSystemEntries(directory)
            .Where(entry => !IsHiddenVaultEntry(Path.GetFileName(entry)))
            .OrderByDescending(Directory.Exists)
            .ThenBy(entry => Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var body = new StringBuilder();
        body.AppendLine("<!doctype html>");
        body.AppendLine("<html lang=\"en\">");
        body.AppendLine("<head>");
        body.AppendLine("<meta charset=\"utf-8\">");
        body.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        body.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        body.AppendLine($"<style>{GetVaultPageCss()}</style>");
        body.AppendLine("</head>");
        body.AppendLine("<body>");
        body.AppendLine("<header>");
        body.AppendLine($"<span class=\"vault-path\">{RenderVaultBreadcrumbs(relativePath)}</span>");
        body.AppendLine("<span class=\"spacer\"></span>");
        if (!string.IsNullOrWhiteSpace(relativePath))
            body.AppendLine($"<a href=\"{WebUtility.HtmlEncode(BuildVaultUrl(parentPath))}\">Parent</a>");
        body.AppendLine("</header>");
        body.AppendLine("<main>");
        body.AppendLine($"<h1>{WebUtility.HtmlEncode(title)}</h1>");

        if (!string.IsNullOrWhiteSpace(relativePath))
            body.AppendLine($"<p><a href=\"{WebUtility.HtmlEncode(BuildVaultUrl(GetVaultParentRelativePath(relativePath)))}\">../</a></p>");

        body.AppendLine("<ul class=\"vault-list\">");
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var entryRelativePath = GetVaultRelativePath(entry);
            var isDirectory = Directory.Exists(entry);
            var label = isDirectory ? name + "/" : name;
            var href = BuildVaultUrl(entryRelativePath);
            var meta = isDirectory ? "folder" : FormatBytes(new FileInfo(entry).Length);
            body.AppendLine($"<li><a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(label)}</a> <span class=\"muted\">{WebUtility.HtmlEncode(meta)}</span></li>");
        }
        body.AppendLine("</ul>");
        body.AppendLine("</main>");
        body.AppendLine("</body>");
        body.AppendLine("</html>");
        return body.ToString();
    }

    private static string RenderVaultMarkdownPage(string markdown, string file, string relativePath)
    {
        var title = Path.GetFileName(relativePath);
        var parentPath = GetVaultParentRelativePath(relativePath);
        var html = RenderMarkdown(markdown ?? string.Empty, file);
        var body = new StringBuilder();

        body.AppendLine("<!doctype html>");
        body.AppendLine("<html lang=\"en\">");
        body.AppendLine("<head>");
        body.AppendLine("<meta charset=\"utf-8\">");
        body.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        body.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        body.AppendLine($"<style>{GetVaultPageCss()}</style>");
        body.AppendLine("</head>");
        body.AppendLine("<body>");
        body.AppendLine("<header>");
        body.AppendLine($"<span class=\"vault-path\">{RenderVaultBreadcrumbs(relativePath)}</span>");
        body.AppendLine("<span class=\"spacer\"></span>");
        body.AppendLine($"<a href=\"{WebUtility.HtmlEncode(BuildVaultUrl(parentPath))}\">Parent</a>");
        body.AppendLine($"<a href=\"{WebUtility.HtmlEncode(BuildVaultUrl(relativePath) + "?raw=1")}\">Raw</a>");
        body.AppendLine("</header>");
        body.AppendLine("<main>");
        body.AppendLine($"<h1>{WebUtility.HtmlEncode(title)}</h1>");
        body.AppendLine("<article>");
        body.AppendLine(html);
        body.AppendLine("</article>");
        body.AppendLine("</main>");
        body.AppendLine("</body>");
        body.AppendLine("</html>");
        return body.ToString();
    }

    private static string RenderVaultBreadcrumbs(string relativePath)
    {
        var parts = (relativePath ?? string.Empty)
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var body = new StringBuilder();
        body.Append("<a href=\"/vault\">Vault</a>");

        var current = string.Empty;
        foreach (var part in parts)
        {
            current = string.IsNullOrWhiteSpace(current) ? part : Path.Combine(current, part);
            body.Append(" / ");
            body.Append($"<a href=\"{WebUtility.HtmlEncode(BuildVaultUrl(current))}\">{WebUtility.HtmlEncode(part)}</a>");
        }

        return body.ToString();
    }

    private static string GetVaultPageCss()
    {
        return "body{font-family:Segoe UI,Arial,sans-serif;line-height:1.55;margin:0;color:#1f2328;background:#fff}" +
            "header{position:sticky;top:0;background:#f6f8fa;border-bottom:1px solid #d0d7de;padding:12px 24px;display:flex;gap:12px;align-items:center;flex-wrap:wrap}" +
            "main{max-width:920px;margin:24px;padding:0 0 48px}" +
            "a{color:#0759b8;text-decoration:none}a:hover{text-decoration:underline}" +
            "article{overflow-wrap:anywhere}" +
            "h1,h2,h3,h4,h5,h6{line-height:1.25;margin:1.25em 0 .5em}" +
            "p{margin:.65em 0}" +
            "ul{padding-left:1.4rem}li{margin:.25rem 0}" +
            "pre{background:#f6f8fa;border:1px solid #d0d7de;border-radius:6px;padding:12px;overflow:auto}" +
            "code{background:#f6f8fa;border-radius:4px;padding:.1rem .25rem}" +
            "pre code{background:transparent;padding:0}" +
            "blockquote{border-left:4px solid #d0d7de;color:#57606a;margin-left:0;padding-left:12px}" +
            ".vault-path{font-weight:600}.spacer{flex:1}.muted{color:#57606a;font-size:.9rem}.vault-list{padding-left:1.4rem}";
    }

    private static string RenderMarkdown(string markdown, string file)
    {
        var currentDirectory = Path.GetDirectoryName(file) ?? GetVaultRoot();
        var body = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCodeBlock = false;
        var inList = false;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
                return;

            body.AppendLine($"<p>{string.Join("<br>", paragraph.Select(line => RenderInlineMarkdown(line, currentDirectory)))}</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (!inList)
                return;

            body.AppendLine("</ul>");
            inList = false;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine ?? string.Empty;
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                body.AppendLine(inCodeBlock ? "</code></pre>" : "<pre><code>");
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                body.AppendLine(WebUtility.HtmlEncode(line));
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^[-*_]{3,}$"))
            {
                if (paragraph.Count == 1)
                {
                    CloseList();
                    body.AppendLine($"<h2>{RenderInlineMarkdown(paragraph[0], currentDirectory)}</h2>");
                    paragraph.Clear();
                    continue;
                }

                FlushParagraph();
                CloseList();
                body.AppendLine("<hr>");
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^=+$") && paragraph.Count == 1)
            {
                CloseList();
                body.AppendLine($"<h1>{RenderInlineMarkdown(paragraph[0], currentDirectory)}</h1>");
                paragraph.Clear();
                continue;
            }

            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.+)$");
            if (heading.Success)
            {
                FlushParagraph();
                CloseList();
                var level = heading.Groups[1].Value.Length;
                body.AppendLine($"<h{level}>{RenderInlineMarkdown(heading.Groups[2].Value, currentDirectory)}</h{level}>");
                continue;
            }

            var list = Regex.Match(trimmed, @"^[-*]\s+(.+)$");
            if (list.Success)
            {
                FlushParagraph();
                if (!inList)
                {
                    body.AppendLine("<ul>");
                    inList = true;
                }
                body.AppendLine($"<li>{RenderInlineMarkdown(list.Groups[1].Value, currentDirectory)}</li>");
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                body.AppendLine($"<blockquote>{RenderInlineMarkdown(trimmed.Substring(2), currentDirectory)}</blockquote>");
                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph();
        CloseList();
        if (inCodeBlock)
            body.AppendLine("</code></pre>");

        return body.ToString();
    }

    private static string RenderInlineMarkdown(string text, string currentDirectory)
    {
        var body = new StringBuilder();
        var plain = new StringBuilder();
        var i = 0;

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;

            body.Append(RenderInlineText(plain.ToString()));
            plain.Clear();
        }

        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '[' && text[i + 1] == '[')
            {
                var end = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    FlushPlain();
                    var target = text.Substring(i + 2, end - i - 2);
                    var parts = target.Split(new[] { '|' }, 2);
                    var href = ResolveVaultWikiLink(parts[0], currentDirectory);
                    var label = parts.Length > 1 ? parts[1] : parts[0];
                    body.Append($"<a href=\"{WebUtility.HtmlEncode(href)}\">{RenderInlineText(label)}</a>");
                    i = end + 2;
                    continue;
                }
            }

            if (text[i] == '[' && (i == 0 || text[i - 1] != '!'))
            {
                var labelEnd = text.IndexOf(']', i + 1);
                if (labelEnd > i && labelEnd + 1 < text.Length && text[labelEnd + 1] == '(')
                {
                    var urlEnd = text.IndexOf(')', labelEnd + 2);
                    if (urlEnd > labelEnd)
                    {
                        FlushPlain();
                        var label = text.Substring(i + 1, labelEnd - i - 1);
                        var href = ResolveVaultMarkdownLink(text.Substring(labelEnd + 2, urlEnd - labelEnd - 2), currentDirectory);
                        body.Append($"<a href=\"{WebUtility.HtmlEncode(href)}\">{RenderInlineText(label)}</a>");
                        i = urlEnd + 1;
                        continue;
                    }
                }
            }

            plain.Append(text[i]);
            i++;
        }

        FlushPlain();
        return body.ToString();
    }

    private static string RenderInlineText(string text)
    {
        var encoded = WebUtility.HtmlEncode(text ?? string.Empty);
        encoded = Regex.Replace(encoded, @"`([^`]+)`", "<code>$1</code>");
        encoded = Regex.Replace(encoded, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, @"\*([^*]+)\*", "<em>$1</em>");
        return encoded;
    }

    private static string ResolveVaultMarkdownLink(string target, string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "#";
        if (Regex.IsMatch(target, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase) || target.StartsWith("#", StringComparison.Ordinal))
            return target;
        if (target.StartsWith("/vault", StringComparison.OrdinalIgnoreCase))
            return target;

        var parts = target.Split(new[] { '#' }, 2);
        var pathPart = Uri.UnescapeDataString(parts[0]).Replace('/', Path.DirectorySeparatorChar);
        var fragment = parts.Length > 1 ? "#" + Uri.EscapeDataString(parts[1]) : string.Empty;
        var resolved = Path.GetFullPath(Path.Combine(currentDirectory, pathPart));
        return TryGetVaultRelativePathAllowRoot(resolved, out var relative)
            ? BuildVaultUrl(relative) + fragment
            : "#";
    }

    private static string ResolveVaultWikiLink(string target, string currentDirectory)
    {
        var clean = (target ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return "#";

        var fileName = Path.GetExtension(clean).Equals(".md", StringComparison.OrdinalIgnoreCase) ? clean : clean + ".md";
        var local = Path.GetFullPath(Path.Combine(currentDirectory, fileName.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(local) && TryGetVaultRelativePathAllowRoot(local, out var localRelative))
            return BuildVaultUrl(localRelative);

        var root = GetVaultRoot();
        var match = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), clean, StringComparison.OrdinalIgnoreCase));
        return match != null && TryGetVaultRelativePathAllowRoot(match, out var relative)
            ? BuildVaultUrl(relative)
            : BuildVaultUrl(GetVaultRelativePath(Path.Combine(currentDirectory, fileName)));
    }

    private static string RenderVaultErrorPage(string message)
    {
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Vault Error</title></head><body>" +
            $"<h1>Vault Error</h1><p>{WebUtility.HtmlEncode(message)}</p><p><a href=\"/vault\">Vault root</a></p>" +
            "</body></html>";
    }

    private static string BuildVaultUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "/vault";

        return "/vault/" + string.Join("/", relativePath
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    private static bool TryGetVaultRelativePath(string path, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var root = GetVaultRoot();
        var fullPath = Path.GetFullPath(path);
        if (!IsUnderDirectory(fullPath, root))
            return false;

        relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(relative);
    }

    private static bool TryGetVaultRelativePathAllowRoot(string path, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var root = GetVaultRoot();
        var fullPath = Path.GetFullPath(path);
        if (!IsSameOrUnderDirectory(fullPath, root))
            return false;

        relative = fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return true;
    }

    private static string GetVaultRoot()
    {
        return Path.GetFullPath(PromptResolver.BasePath);
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrUnderDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
            IsUnderDirectory(path, directory);
    }

    private static string GetContentType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".html":
            case ".htm":
                return "text/html; charset=utf-8";
            case ".md":
            case ".txt":
            case ".log":
            case ".json":
            case ".jsonl":
            case ".csv":
                return "text/plain; charset=utf-8";
            case ".png":
                return "image/png";
            case ".jpg":
            case ".jpeg":
                return "image/jpeg";
            case ".gif":
                return "image/gif";
            case ".webp":
                return "image/webp";
            case ".wav":
                return "audio/wav";
            case ".mp3":
                return "audio/mpeg";
            default:
                return "application/octet-stream";
        }
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

    private static DateTime ParseDate(Dictionary<string, string> query)
    {
        if (query != null && query.TryGetValue("date", out var raw) && DateTime.TryParse(raw, out var date))
            return date.Date;
        return DateTime.Today;
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

    private readonly struct PendingIdeaRequest
    {
        public readonly string slug;
        public readonly string prompt;

        public PendingIdeaRequest(string slug, string prompt)
        {
            this.slug = slug;
            this.prompt = prompt;
        }
    }
}

[Serializable]
public sealed class GeneratorRuntimeInfo
{
    [NonSerialized] internal ChatGenerator generator;

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
    private static readonly List<MemoryDiagnosticsSnapshot> memorySnapshots = new List<MemoryDiagnosticsSnapshot>();
    private static readonly LinkedList<string> touchedBackgrounds = new LinkedList<string>();
    private static readonly LinkedList<string> touchedProps = new LinkedList<string>();
    private static readonly LinkedList<string> touchedSoundGroups = new LinkedList<string>();
    private static readonly HashSet<string> touchedBackgroundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> touchedPropKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> touchedSoundGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private const int MaxEvents = 300;
    private const int MaxEpisodes = 150;
    private const int MaxMemorySnapshots = 50;
    private const int MaxRecentTouchedAssets = 20;

    private static MemoryDiagnosticsSnapshot latestMemorySnapshot;
    private static bool memorySnapshotRequested;
    private static string pendingMemorySnapshotReason;

    public static void RecordEvent(string type, string message, ChatManagerContext context = null, string channelKey = null, string episodeSlug = null, DateTimeOffset? countdownAt = null)
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
            timestamp = DateTimeOffset.Now.ToString("O"),
            countdownAt = countdownAt?.ToString("O") ?? string.Empty
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
                synopsis = string.Empty,
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
                synopsis = chat.Synopsis ?? string.Empty,
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
                    synopsis = string.Empty,
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
                synopsis = chat.Synopsis ?? string.Empty,
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
                synopsis = chat.Synopsis ?? string.Empty,
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

    public static MemoryDiagnosticsSnapshot CaptureMemorySnapshot(string reason = null)
    {
        var snapshot = BuildMemorySnapshot(reason);

        lock (sync)
        {
            latestMemorySnapshot = snapshot;
            memorySnapshots.Add(snapshot);
            TrimMemorySnapshotsUnsafe();
            return snapshot;
        }
    }

    public static void RequestMemorySnapshot(string reason = null)
    {
        lock (sync)
        {
            memorySnapshotRequested = true;
            pendingMemorySnapshotReason = string.IsNullOrWhiteSpace(reason) ? "requested" : reason;
        }
    }

    public static void CaptureRequestedMemorySnapshot()
    {
        string reason;

        lock (sync)
        {
            if (!memorySnapshotRequested)
                return;

            memorySnapshotRequested = false;
            reason = pendingMemorySnapshotReason;
            pendingMemorySnapshotReason = null;
        }

        CaptureMemorySnapshot(reason);
    }

    public static MemoryDiagnosticsSnapshot GetLatestMemorySnapshot()
    {
        lock (sync)
        {
            if (latestMemorySnapshot != null)
                return latestMemorySnapshot;
        }

        return BuildFallbackMemorySnapshot();
    }

    public static IReadOnlyList<MemoryDiagnosticsSnapshot> GetRecentMemorySnapshots(int limit = 20)
    {
        lock (sync)
        {
            return memorySnapshots
                .OrderByDescending(s => s.timestamp)
                .Take(NormalizeLimit(limit, 100))
                .ToList();
        }
    }

    public static void RecordTouchedBackground(string key)
    {
        RecordTouchedAsset(key, touchedBackgroundKeys, touchedBackgrounds);
    }

    public static void RecordTouchedProp(string key)
    {
        RecordTouchedAsset(key, touchedPropKeys, touchedProps);
    }

    public static void RecordTouchedSoundGroup(string key)
    {
        RecordTouchedAsset(key, touchedSoundGroupKeys, touchedSoundGroups);
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
        existing.synopsis = Prefer(incoming.synopsis, existing.synopsis);
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

    private static void TrimMemorySnapshotsUnsafe()
    {
        if (memorySnapshots.Count <= MaxMemorySnapshots)
            return;

        memorySnapshots.RemoveRange(0, memorySnapshots.Count - MaxMemorySnapshots);
    }

    private static void RecordTouchedAsset(string key, HashSet<string> keys, LinkedList<string> recent)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (sync)
        {
            keys.Add(key);
            recent.AddFirst(key);
            while (recent.Count > MaxRecentTouchedAssets)
                recent.RemoveLast();
        }
    }

    private static MemoryDiagnosticsSnapshot BuildMemorySnapshot(string reason)
    {
        var process = DiagnosticsProcess.GetCurrentProcess();
        var manager = ChatManager.Instance;
        var nowPlaying = manager?.NowPlaying;
        var nodes = nowPlaying?.Nodes ?? new List<ChatNode>();
        var currentScene = SceneManager.GetActiveScene();
        var bucketEntries = MemoryBucket.Buckets
            .Select(kvp => new MemoryBucketSummaryRecord
            {
                key = kvp.Key,
                memoryCount = kvp.Value?.Memories?.Count ?? 0,
                estimatedEmbeddingBytes = EstimateEmbeddingBytes(kvp.Value)
            })
            .OrderByDescending(entry => entry.estimatedEmbeddingBytes)
            .ThenByDescending(entry => entry.memoryCount)
            .Take(5)
            .ToList();

        long totalEmbeddingBytes = 0;
        int totalMemoryCount = 0;
        foreach (var bucket in MemoryBucket.Buckets.Values)
        {
            if (bucket?.Memories == null)
                continue;

            totalMemoryCount += bucket.Memories.Count;
            totalEmbeddingBytes += EstimateEmbeddingBytes(bucket);
        }

        var soccer = SoccerGameSource.Instance;
        var announcer = soccer?.AnnouncerDiagnostics;
        var assetCache = RuntimeAssetCache.GetDiagnostics();

        lock (sync)
        {
            return new MemoryDiagnosticsSnapshot
            {
                timestamp = DateTimeOffset.Now.ToString("O"),
                reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason,
                process = new ProcessMemoryRecord
                {
                    workingSetBytes = GetBestWorkingSetBytes(process),
                    privateMemoryBytes = process.PrivateMemorySize64,
                    managedHeapBytes = GC.GetTotalMemory(false),
                    gcGen0Collections = GC.CollectionCount(0),
                    gcGen1Collections = GC.CollectionCount(1),
                    gcGen2Collections = GC.CollectionCount(2)
                },
                runtime = new RuntimeMemoryRecord
                {
                    currentContextKey = manager?.CurrentContext?.Key ?? string.Empty,
                    currentContextName = manager?.CurrentContext?.Name ?? string.Empty,
                    currentSceneName = currentScene.name ?? string.Empty,
                    trackedContextCount = manager?.Contexts?.Count ?? 0,
                    actorsInSceneCount = manager?.ActorsInScene?.Count ?? 0,
                    playlistDepth = manager?.PlayList?.Count ?? 0,
                    readyForAction = manager?.ReadyForAction ?? false,
                    isPaused = ChatManager.IsPaused
                },
                chat = new ChatMemoryRecord
                {
                    episodeSlug = nowPlaying?.FileName ?? string.Empty,
                    episodeTitle = nowPlaying?.Title ?? string.Empty,
                    totalNodeCount = nodes.Count,
                    newNodeCount = nodes.Count(node => node?.New == true),
                    nodesWithAudioData = nodes.Count(node => !string.IsNullOrEmpty(node?.AudioData)),
                    nodesWithHydratedRuntimeAudio = nodes.Count(node => node?.HasRuntimeAudioClip == true),
                    totalAudioDataChars = nodes
                        .Where(node => !string.IsNullOrEmpty(node?.AudioData))
                        .Sum(node => (long)node.AudioData.Length),
                    nodesWithTextureData = string.IsNullOrEmpty(nowPlaying?.TextureData) ? 0 : 1,
                    totalTextureDataChars = nowPlaying?.TextureData?.Length ?? 0
                },
                memoryBuckets = new MemoryBucketDiagnosticsRecord
                {
                    bucketCount = MemoryBucket.Buckets.Count,
                    totalMemoryCount = totalMemoryCount,
                    estimatedEmbeddingBytes = totalEmbeddingBytes,
                    largestBuckets = bucketEntries
                },
                soccer = new SoccerMemoryRecord
                {
                    available = soccer != null,
                    isSceneLoaded = soccer?.IsSceneLoaded ?? false,
                    isGameLoaded = soccer?.IsGameLoaded ?? false,
                    addedSceneCount = soccer?.AddedSceneCount ?? 0,
                    announcerQueueCount = announcer?.QueueCount ?? 0,
                    announcerClipCacheCount = announcer?.ClipCacheCount ?? 0,
                    announcerMatchActive = announcer?.MatchActive ?? false,
                    currentMatchId = soccer?.CurrentMatchId ?? string.Empty
                },
                resources = new ResourceTouchRecord
                {
                    assetCacheOwnerCount = assetCache.ownerCount,
                    totalCachedAssetCount = assetCache.totalCachedAssetCount,
                    liveBackgroundCount = assetCache.liveBackgroundCount,
                    liveFlagCount = assetCache.liveFlagCount,
                    livePropCount = assetCache.livePropCount,
                    liveSoundGroupCount = assetCache.liveSoundGroupCount,
                    touchedBackgroundCount = touchedBackgroundKeys.Count,
                    touchedPropCount = touchedPropKeys.Count,
                    touchedSoundGroupCount = touchedSoundGroupKeys.Count,
                    recentBackgrounds = touchedBackgrounds.Take(MaxRecentTouchedAssets).ToList(),
                    recentProps = touchedProps.Take(MaxRecentTouchedAssets).ToList(),
                    recentSoundGroups = touchedSoundGroups.Take(MaxRecentTouchedAssets).ToList()
                }
            };
        }
    }

    private static MemoryDiagnosticsSnapshot BuildFallbackMemorySnapshot()
    {
        var process = DiagnosticsProcess.GetCurrentProcess();
        return new MemoryDiagnosticsSnapshot
        {
            timestamp = DateTimeOffset.Now.ToString("O"),
            reason = "snapshot_unavailable",
            process = new ProcessMemoryRecord
            {
                workingSetBytes = GetBestWorkingSetBytes(process),
                privateMemoryBytes = process.PrivateMemorySize64,
                managedHeapBytes = GC.GetTotalMemory(false),
                gcGen0Collections = GC.CollectionCount(0),
                gcGen1Collections = GC.CollectionCount(1),
                gcGen2Collections = GC.CollectionCount(2)
            },
            runtime = new RuntimeMemoryRecord(),
            chat = new ChatMemoryRecord(),
            memoryBuckets = new MemoryBucketDiagnosticsRecord
            {
                largestBuckets = new List<MemoryBucketSummaryRecord>()
            },
            soccer = new SoccerMemoryRecord(),
            resources = new ResourceTouchRecord
            {
                assetCacheOwnerCount = 0,
                totalCachedAssetCount = 0,
                liveBackgroundCount = 0,
                liveFlagCount = 0,
                livePropCount = 0,
                liveSoundGroupCount = 0,
                recentBackgrounds = new List<string>(),
                recentProps = new List<string>(),
                recentSoundGroups = new List<string>()
            }
        };
    }

    private static long EstimateEmbeddingBytes(MemoryBucket bucket)
    {
        if (bucket?.Memories == null)
            return 0;

        long bytes = 0;
        foreach (var memory in bucket.Memories)
            bytes += (long)(memory?.Embeddings?.Length ?? 0) * sizeof(double);
        return bytes;
    }

    private static long GetBestWorkingSetBytes(DiagnosticsProcess process)
    {
        try
        {
            process?.Refresh();
        }
        catch
        {
            // Some Unity player targets restrict process counters; fall through to cheaper runtime fallbacks.
        }

        var workingSet = process?.WorkingSet64 ?? 0;
        if (workingSet > 0)
            return workingSet;

        workingSet = Environment.WorkingSet;
        if (workingSet > 0)
            return workingSet;

        var privateMemory = process?.PrivateMemorySize64 ?? 0;
        if (privateMemory > 0)
            return privateMemory;

        return GC.GetTotalMemory(false);
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
            synopsis = episode.synopsis,
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
    public string countdownAt;
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
    public string synopsis;
    public string generatedAt;
    public string queuedAt;
    public string playedAt;
    public string status;
}

[Serializable]
public sealed class MemoryDiagnosticsSnapshot
{
    public string timestamp;
    public string reason;
    public ProcessMemoryRecord process;
    public RuntimeMemoryRecord runtime;
    public ChatMemoryRecord chat;
    public MemoryBucketDiagnosticsRecord memoryBuckets;
    public SoccerMemoryRecord soccer;
    public ResourceTouchRecord resources;
}

[Serializable]
public sealed class ProcessMemoryRecord
{
    public long workingSetBytes;
    public long privateMemoryBytes;
    public long managedHeapBytes;
    public int gcGen0Collections;
    public int gcGen1Collections;
    public int gcGen2Collections;
}

[Serializable]
public sealed class RuntimeMemoryRecord
{
    public string currentContextKey;
    public string currentContextName;
    public string currentSceneName;
    public int trackedContextCount;
    public int actorsInSceneCount;
    public int playlistDepth;
    public bool readyForAction;
    public bool isPaused;
}

[Serializable]
public sealed class ChatMemoryRecord
{
    public string episodeSlug;
    public string episodeTitle;
    public int totalNodeCount;
    public int newNodeCount;
    public int nodesWithAudioData;
    public int nodesWithHydratedRuntimeAudio;
    public long totalAudioDataChars;
    public int nodesWithTextureData;
    public long totalTextureDataChars;
}

[Serializable]
public sealed class MemoryBucketDiagnosticsRecord
{
    public int bucketCount;
    public int totalMemoryCount;
    public long estimatedEmbeddingBytes;
    public List<MemoryBucketSummaryRecord> largestBuckets;
}

[Serializable]
public sealed class MemoryBucketSummaryRecord
{
    public string key;
    public int memoryCount;
    public long estimatedEmbeddingBytes;
}

[Serializable]
public sealed class SoccerMemoryRecord
{
    public bool available;
    public bool isSceneLoaded;
    public bool isGameLoaded;
    public int addedSceneCount;
    public int announcerQueueCount;
    public int announcerClipCacheCount;
    public bool announcerMatchActive;
    public string currentMatchId;
}

[Serializable]
public sealed class ResourceTouchRecord
{
    public int assetCacheOwnerCount;
    public int totalCachedAssetCount;
    public int liveBackgroundCount;
    public int liveFlagCount;
    public int livePropCount;
    public int liveSoundGroupCount;
    public int touchedBackgroundCount;
    public int touchedPropCount;
    public int touchedSoundGroupCount;
    public List<string> recentBackgrounds;
    public List<string> recentProps;
    public List<string> recentSoundGroups;
}
