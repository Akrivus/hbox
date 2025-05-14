#if SOCCER
using FStudio.MatchEngine;
#endif
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using UnityEngine;

public class DiscordManager : MonoBehaviour, IConfigurable<DiscordConfigs>
{
    public static Dictionary<string, DiscordWebhook> Webhooks => webhooks;
    private static Dictionary<string, DiscordWebhook> webhooks;

    private static Queue<KeyValuePair<string, DiscordWebhookMessage>> Q = new Queue<KeyValuePair<string, DiscordWebhookMessage>>();

    public Dictionary<string, string> WebhookURLs { get; private set; }

    private static string url = "https://raw.githubusercontent.com/Akrivus/polbol/refs/heads/main/WWW/";

    public void Configure(DiscordConfigs c)
    {
        WebhookURLs = c.WebhookURLs;
        webhooks = WebhookURLs.ToDictionary(k => k.Key, v => new DiscordWebhook(v.Value));
        url = c.AvatarURL;
        ChatManager.Instance.OnChatNodeActivated += SendDialogue;

#if SOCCER
        var narrator = FindFirstObjectByType<Narrator>();
        if (narrator != null)
            narrator.OnNarration += SendNarration;

        SoccerGameSource.Instance.OnEmit += SendSportsUpdates;
#endif

        StartCoroutine(UpdateWebhooks());
    }

    private void Awake()
    {
        ConfigManager.Instance.RegisterConfig(typeof(DiscordConfigs), "discord", (config) => Configure((DiscordConfigs) config));
    }

    private void OnApplicationQuit()
    {
        StopAllCoroutines();
    }

    private IEnumerator UpdateWebhooks()
    {
        while (Application.isPlaying)
        {
            yield return new WaitUntilTimer(() => Q.Count > 0, 30);
            
            if (Q.TryDequeue(out var m) && Webhooks.TryGetValue(m.Key, out var web))
                yield return web.SendAsync(m.Value);
        }
    }

    private void SendNarration(string text)
    {
        PutInQueue("#sports", text);
    }

    private void SendDialogue(ChatNode node)
    {
        PutInQueue("#stream", node.Line, node.Actor.Name, GetAvatarURL(node));
    }

    private void SendSportsUpdates(string message)
    {
        if (!message.StartsWith("#")) return;
        PutInQueue("#stream-sports", message);
        PutInQueue("#sports", message);
    }

    private int ToHex(Color color)
    {
        return (int) (color.r * 255) << 16 | (int) (color.g * 255) << 8 | (int) (color.b * 255);
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
        var slug = actor.Name.ToFileSafeString();
        return $"{url}{sentiment.Name}-{slug}.png";
    }

    public static void PutInQueue(string webhook, string content, string username = null, string avatarUrl = null)
    {
        PutInQueue(webhook, new DiscordWebhookMessage(content, username, avatarUrl));
    }

    public static void PutInQueue(string webhook, DiscordWebhookMessage message)
    {
        Q.Enqueue(new KeyValuePair<string, DiscordWebhookMessage>(webhook, message));
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

    public IEnumerator SendAsync(DiscordWebhookMessage message)
    {
        if (rateLimitTimer.Elapsed.Seconds > 2 || requestsRemaining <= 0)
            yield return RateLimit();

        Client = new WebClient();
        Client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
        Client.UploadStringAsync(new Uri(URL), JsonConvert.SerializeObject(message));
        requestsRemaining--;

        yield return new WaitUntil(() => !Client.IsBusy);
    }

    private IEnumerator RateLimit()
    {
        yield return new WaitUntil(() => rateLimitTimer.Elapsed.Seconds > 2);
        rateLimitTimer.Restart();
        requestsRemaining = 5;
    }
}