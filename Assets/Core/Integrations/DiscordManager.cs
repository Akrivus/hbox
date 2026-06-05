using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class DiscordManager : MonoBehaviour, IConfigurable<DiscordConfigs>
{
    public static DiscordManager Instance { get; private set; }

    public static Dictionary<string, DiscordWebhook> Webhooks => webhooks;
    private static Dictionary<string, DiscordWebhook> webhooks;
    private static readonly Dictionary<string, Dictionary<string, DiscordWebhook>> webhooksByContext = new Dictionary<string, Dictionary<string, DiscordWebhook>>(StringComparer.OrdinalIgnoreCase);

    private static Queue<DiscordWebhookQueueItem> Q = new Queue<DiscordWebhookQueueItem>();

    public Dictionary<string, string> WebhookURLs { get; private set; }
    private ChatManagerContext managerContext;

    private static string url = "https://raw.githubusercontent.com/Akrivus/polbol/refs/heads/main/WWW/";

    public void Configure(DiscordConfigs c)
    {
        WebhookURLs = c.WebhookURLs ?? new Dictionary<string, string>();
        webhooks = WebhookURLs.ToDictionary(k => k.Key, v => new DiscordWebhook(v.Value));
        var contextKey = managerContext?.Key ?? ChatManagerContext.Current?.Key;
        if (!string.IsNullOrWhiteSpace(contextKey))
            webhooksByContext[contextKey] = webhooks;
        url = c.AvatarURL;

        StartCoroutine(UpdateWebhooks());
    }

    private void Start()
    {
        managerContext = ChatManagerContext.Current;
        managerContext.ConfigManager.RegisterConfig(typeof(DiscordConfigs), "discord", (_config) => Configure((DiscordConfigs)_config));
        Instance = this;
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private IEnumerator UpdateWebhooks()
    {
        do
        {
            yield return new WaitUntilTimer(() => Q.Count > 0, 30);

            if (Q.TryDequeue(out var item))
            {
                if (item.Webhook == null)
                    continue;

                yield return item.Webhook.SendAsync(item.Message, item.WaitForResponse, item.OnPosted);
            }
        } while (this != null);
    }

    public void SendDialogue(ChatNode node)
    {
        PutInQueue(GetStreamChannel(ChatManagerContext.Current), node.Line, node.Actor.Name, GetAvatarURL(node));
    }

    private string GetAvatarURL(ChatNode node)
    {
        var reaction = node.Reactions.FirstOrDefault(r => r.Actor == node.Actor);
        var sentiment = node.Actor.DefaultSentiment;

        if (reaction != null)
            sentiment = reaction.Sentiment;

        return GetAvatarURL(node.Actor, sentiment);
    }

    public static string GetAvatarURL(Actor actor, Sentiment sentiment)
    {
        return $"{url}{actor.Name.Replace(" ", "%20")}/{sentiment.Name}.png";
    }

    public static void PutInQueue(string webhook, string content, string username = null, string avatarUrl = null)
    {
        PutInQueue(webhook, new DiscordWebhookMessage(content, username, avatarUrl));
    }

    public static void PutInQueue(string channel, DiscordWebhookMessage message)
    {
        PutInQueue(channel, message, false, null);
    }

    public static void PutInQueue(string channel, DiscordWebhookMessage message, Action<DiscordPostedMessage> onPosted)
    {
        PutInQueue(channel, message, true, onPosted);
    }

    public static void PutInQueue(string channel, DiscordWebhookMessage message, bool waitForResponse, Action<DiscordPostedMessage> onPosted)
    {
        var webhook = ResolveWebhook(null, channel);
        Q.Enqueue(new DiscordWebhookQueueItem(webhook, message, waitForResponse, onPosted));
    }

    public static void PutInQueueForContext(string contextKey, string channel, DiscordWebhookMessage message, Action<DiscordPostedMessage> onPosted)
    {
        var webhook = ResolveWebhook(contextKey, channel);
        Q.Enqueue(new DiscordWebhookQueueItem(webhook, message, true, onPosted));
    }

    public static void DeleteWebhookMessageForContext(string contextKey, string channel, string messageId)
    {
        if (Instance == null || string.IsNullOrWhiteSpace(messageId))
            return;

        var webhook = ResolveWebhook(contextKey, channel);
        if (webhook == null)
            return;

        Instance.StartCoroutine(webhook.DeleteMessageAsync(messageId));
    }

    public static string GetStreamChannel(ChatManagerContext context)
    {
        if (webhooks == null || webhooks.Count == 0)
            return "#stream";

        var key = context?.Key;
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (webhooks.ContainsKey(key))
                return key;

            var hashKey = key.StartsWith("#", StringComparison.Ordinal) ? key : "#" + key;
            if (webhooks.ContainsKey(hashKey))
                return hashKey;
        }

        return webhooks.ContainsKey("#stream") ? "#stream" : webhooks.Keys.First();
    }

    private static DiscordWebhook ResolveWebhook(string contextKey, string channel)
    {
        if (!string.IsNullOrWhiteSpace(contextKey) && webhooksByContext.TryGetValue(contextKey, out var contextWebhooks))
        {
            var webhook = ResolveWebhookFromMap(contextWebhooks, contextKey, channel);
            if (webhook != null)
                return webhook;
        }

        return ResolveWebhookFromMap(Webhooks, contextKey, channel);
    }

    private static DiscordWebhook ResolveWebhookFromMap(Dictionary<string, DiscordWebhook> map, string contextKey, string channel)
    {
        if (map == null || map.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(channel) && map.TryGetValue(channel, out var webhook))
            return webhook;

        if (!string.IsNullOrWhiteSpace(contextKey))
        {
            if (map.TryGetValue(contextKey, out webhook))
                return webhook;

            var hashKey = contextKey.StartsWith("#", StringComparison.Ordinal) ? contextKey : "#" + contextKey;
            if (map.TryGetValue(hashKey, out webhook))
                return webhook;
        }

        if (map.TryGetValue("#stream", out webhook))
            return webhook;

        return map.Values.FirstOrDefault();
    }
}

public class DiscordWebhookMessage
{
    [JsonProperty("content")]
    public string Content { get; set; }
    [JsonProperty("username")]
    public string Username { get; set; }
    [JsonProperty("avatar_url")]
    public string Avatar { get; set; }
    [JsonProperty("tts")]
    public bool TTS { get; set; }
    [JsonProperty("embeds")]
    public DiscordEmbed[] Embeds { get; set; }

    public DiscordWebhookMessage(string content, string username, string avatarUrl, params DiscordEmbed[] embeds)
    {
        Content = content;
        Username = username;
        Avatar = avatarUrl;
        Embeds = embeds;
    }

    public DiscordWebhookMessage(params DiscordEmbed[] embeds)
    {
        Embeds = embeds;
    }
}

public class DiscordEmbed
{
    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("url")]
    public string URL { get; set; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonProperty("color")]
    public int Color { get; set; }

    [JsonProperty("footer")]
    public DiscordEmbedFooter Footer { get; set; }

    [JsonProperty("image")]
    public DiscordEmbedImage Image { get; set; }

    [JsonProperty("thumbnail")]
    public DiscordEmbedThumbnail Thumbnail { get; set; }

    [JsonProperty("video")]
    public DiscordEmbedVideo Video { get; set; }

    [JsonProperty("provider")]
    public DiscordEmbedProvider Provider { get; set; }

    [JsonProperty("author")]
    public DiscordEmbedAuthor Author { get; set; }
}

public class DiscordEmbedFooter
{
    [JsonProperty("text")]
    public string Text { get; set; }

    [JsonProperty("icon_url")]
    public string Icon { get; set; }
}

public class DiscordEmbedImage
{
    [JsonProperty("url")]
    public string URL { get; set; }

    [JsonProperty("height")]
    public int Height { get; set; }

    [JsonProperty("width")]
    public int Width { get; set; }
}

public class DiscordEmbedThumbnail
{
    [JsonProperty("url")]
    public string URL { get; set; }

    [JsonProperty("height")]
    public int Height { get; set; }

    [JsonProperty("width")]
    public int Width { get; set; }
}

public class DiscordEmbedVideo
{
    [JsonProperty("url")]
    public string URL { get; set; }

    [JsonProperty("height")]
    public int Height { get; set; }

    [JsonProperty("width")]
    public int Width { get; set; }
}

public class DiscordEmbedProvider
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("url")]
    public string URL { get; set; }
}

public class DiscordEmbedAuthor
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("url")]
    public string URL { get; set; }

    [JsonProperty("icon_url")]
    public string Icon { get; set; }
}

public class DiscordWebhook
{
    public string URL { get; set; }
    public WebClient Client { get; set; }

    private const float RateLimitWindowSeconds = 2.2f;
    private const int MaxRequestsPerWindow = 4;
    private const float MinimumSendSpacingSeconds = 0.75f;

    private Stopwatch rateLimitTimer = new Stopwatch();
    private Stopwatch sendSpacingTimer = new Stopwatch();
    private int requestsRemaining = MaxRequestsPerWindow;

    public DiscordWebhook(string url)
    {
        URL = url;
        rateLimitTimer.Start();
        sendSpacingTimer.Start();
    }

    public IEnumerator SendAsync(DiscordWebhookMessage message, bool waitForResponse = false, Action<DiscordPostedMessage> onPosted = null)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            yield return WaitForSendSlot();

            Client = new WebClient();
            Client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
            UploadStringCompletedEventArgs result = null;
            UploadStringCompletedEventHandler handler = (_, args) => result = args;
            Client.UploadStringCompleted += handler;

            var targetUrl = waitForResponse
                ? $"{URL}{(URL.Contains("?") ? "&" : "?")}wait=true"
                : URL;
            Client.UploadStringAsync(new Uri(targetUrl), "POST", JsonConvert.SerializeObject(message));
            requestsRemaining--;
            sendSpacingTimer.Restart();

            yield return new WaitUntil(() => result != null);
            Client.UploadStringCompleted -= handler;

            if (result.Error != null)
            {
                if (DiscordRateLimit.TryGetRetryDelay(result.Error, out var retryDelay) && attempt < maxAttempts)
                {
                    UnityEngine.Debug.LogWarning($"Discord webhook rate-limited; retrying in {retryDelay.TotalSeconds:0.##}s.");
                    yield return new WaitForSeconds((float)retryDelay.TotalSeconds);
                    ResetRateLimitWindow();
                    continue;
                }

                UnityEngine.Debug.LogError(result.Error);
                yield break;
            }

            if (waitForResponse && !string.IsNullOrWhiteSpace(result.Result))
            {
                DiscordPostedMessage postedMessage = null;

                try
                {
                    postedMessage = JsonConvert.DeserializeObject<DiscordPostedMessage>(result.Result);
                }
                catch (JsonException e)
                {
                    UnityEngine.Debug.LogWarning($"DiscordWebhook.SendAsync failed to parse webhook response: {e.Message}");
                }

                onPosted?.Invoke(postedMessage);
            }

            yield break;
        }
    }

    public IEnumerator DeleteMessageAsync(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            yield break;

        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            yield return WaitForSendSlot();

            Client = new WebClient();
            UploadStringCompletedEventArgs result = null;
            UploadStringCompletedEventHandler handler = (_, args) => result = args;
            Client.UploadStringCompleted += handler;

            var targetUrl = $"{URL.TrimEnd('/')}/messages/{Uri.EscapeDataString(messageId)}";
            Client.UploadStringAsync(new Uri(targetUrl), "DELETE", string.Empty);
            requestsRemaining--;
            sendSpacingTimer.Restart();

            yield return new WaitUntil(() => result != null);
            Client.UploadStringCompleted -= handler;

            if (result.Error != null)
            {
                if (IsNotFound(result.Error))
                    yield break;

                if (DiscordRateLimit.TryGetRetryDelay(result.Error, out var retryDelay) && attempt < maxAttempts)
                {
                    UnityEngine.Debug.LogWarning($"Discord webhook rate-limited; retrying delete in {retryDelay.TotalSeconds:0.##}s.");
                    yield return new WaitForSeconds((float)retryDelay.TotalSeconds);
                    ResetRateLimitWindow();
                    continue;
                }

                UnityEngine.Debug.LogWarning($"DiscordWebhook.DeleteMessageAsync failed: {result.Error.Message}");
                yield break;
            }

            yield break;
        }
    }

    private static bool IsNotFound(Exception error)
    {
        return error is WebException webException &&
            webException.Response is HttpWebResponse response &&
            response.StatusCode == HttpStatusCode.NotFound;
    }

    private IEnumerator RateLimit()
    {
        yield return new WaitUntil(() => rateLimitTimer.Elapsed.TotalSeconds >= RateLimitWindowSeconds);
        ResetRateLimitWindow();
    }

    private IEnumerator WaitForSendSlot()
    {
        if (rateLimitTimer.Elapsed.TotalSeconds >= RateLimitWindowSeconds)
            ResetRateLimitWindow();

        if (requestsRemaining <= 0)
            yield return RateLimit();

        var spacingDelay = MinimumSendSpacingSeconds - (float)sendSpacingTimer.Elapsed.TotalSeconds;
        if (spacingDelay > 0f)
            yield return new WaitForSeconds(spacingDelay);

        if (rateLimitTimer.Elapsed.TotalSeconds >= RateLimitWindowSeconds)
            ResetRateLimitWindow();
    }

    private void ResetRateLimitWindow()
    {
        rateLimitTimer.Restart();
        requestsRemaining = MaxRequestsPerWindow;
    }

}

public static class DiscordRateLimit
{
    public static bool TryGetRetryDelay(Exception error, out TimeSpan retryDelay)
    {
        retryDelay = TimeSpan.FromSeconds(1);
        return error is WebException webException && TryGetRetryDelay(webException, out retryDelay);
    }

    public static bool TryGetRetryDelay(WebException exception, out TimeSpan retryDelay)
    {
        retryDelay = TimeSpan.FromSeconds(1);
        if (exception?.Response is not HttpWebResponse response)
            return false;
        if ((int)response.StatusCode != 429)
            return false;

        if (double.TryParse(response.Headers["Retry-After"], NumberStyles.Float, CultureInfo.InvariantCulture, out var headerDelay))
        {
            retryDelay = TimeSpan.FromSeconds(Math.Max(0.25, headerDelay));
            return true;
        }

        try
        {
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            var body = reader.ReadToEnd();
            var retryAfter = JObject.Parse(body).Value<double?>("retry_after");
            if (retryAfter.HasValue)
                retryDelay = TimeSpan.FromSeconds(Math.Max(0.25, retryAfter.Value));
        }
        catch
        {
            retryDelay = TimeSpan.FromSeconds(1);
        }

        return true;
    }
}

public sealed class DiscordPostedMessage
{
    [JsonProperty("id")]
    public string id { get; set; }

    [JsonProperty("channel_id")]
    public string channel_id { get; set; }
}

public sealed class DiscordWebhookQueueItem
{
    public DiscordWebhookQueueItem(DiscordWebhook webhook, DiscordWebhookMessage message, bool waitForResponse, Action<DiscordPostedMessage> onPosted)
    {
        Webhook = webhook;
        Message = message;
        WaitForResponse = waitForResponse;
        OnPosted = onPosted;
    }

    public DiscordWebhook Webhook { get; }
    public DiscordWebhookMessage Message { get; }
    public bool WaitForResponse { get; }
    public Action<DiscordPostedMessage> OnPosted { get; }
}
