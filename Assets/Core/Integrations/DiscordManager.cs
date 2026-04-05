using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using UnityEngine;

public class DiscordManager : MonoBehaviour, IConfigurable<DiscordConfigs>
{
    public static DiscordManager Instance { get; private set; }

    public static Dictionary<string, DiscordWebhook> Webhooks => webhooks;
    private static Dictionary<string, DiscordWebhook> webhooks;

    private static Queue<DiscordWebhookQueueItem> Q = new Queue<DiscordWebhookQueueItem>();

    public Dictionary<string, string> WebhookURLs { get; private set; }

    private static string url = "https://raw.githubusercontent.com/Akrivus/polbol/refs/heads/main/WWW/";

    public void Configure(DiscordConfigs c)
    {
        WebhookURLs = c.WebhookURLs;
        webhooks = WebhookURLs.ToDictionary(k => k.Key, v => new DiscordWebhook(v.Value));
        url = c.AvatarURL;

        StartCoroutine(UpdateWebhooks());
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(DiscordConfigs), "discord", (_config) => Configure((DiscordConfigs)_config));
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
        PutInQueue("#stream", node.Line, node.Actor.Name, GetAvatarURL(node));
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
        var webhook = Webhooks.FirstOrDefault(w => w.Key == channel).Value;
        Q.Enqueue(new DiscordWebhookQueueItem(webhook, message, waitForResponse, onPosted));
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

    private Stopwatch rateLimitTimer = new Stopwatch();
    private int requestsRemaining = 5;

    public DiscordWebhook(string url)
    {
        URL = url;
        rateLimitTimer.Start();
    }

    public IEnumerator SendAsync(DiscordWebhookMessage message, bool waitForResponse = false, Action<DiscordPostedMessage> onPosted = null)
    {
        if (rateLimitTimer.Elapsed.TotalSeconds > 2 || requestsRemaining <= 0)
            yield return RateLimit();

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

        yield return new WaitUntil(() => result != null);
        Client.UploadStringCompleted -= handler;

        if (result.Error != null)
        {
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
    }

    private IEnumerator RateLimit()
    {
        yield return new WaitUntil(() => rateLimitTimer.Elapsed.Seconds > 2);
        rateLimitTimer.Restart();
        requestsRemaining = 5;
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
