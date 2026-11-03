using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public static class DiscordSubmissionMessage
{
    public static WebSourceDocument FromInteraction(JToken interaction, Idea idea)
    {
        var id = interaction?.Value<string>("id");
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("The submission interaction ID is missing.");
        var channel = interaction.Value<string>("channel_id") ?? interaction["channel"]?.Value<string>("id");
        var guild = interaction.Value<string>("guild_id") ?? "@me";
        return new WebSourceDocument
        {
            provider = "discord", id = "command:" + id, author = idea.Author, selftext = idea.Prompt,
            title = idea.Prompt.Substring(0, Math.Min(idea.Prompt.Length, 200)),
            url = string.IsNullOrEmpty(channel) ? null : $"https://discord.com/channels/{guild}/{channel}",
            fetchedAtUtc = DateTime.UtcNow.ToString("O"), images = idea.Images
        };
    }

    public static WebSourceDocument Parse(JToken message)
    {
        var id = message?.Value<string>("id");
        var channel = message?.Value<string>("channel_id");
        var guild = message?.Value<string>("guild_id");
        var type = message?.Value<int?>("type") ?? 0;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(guild) ||
            message["author"]?.Value<bool?>("bot") == true || message["webhook_id"] != null || (type != 0 && type != 19)) return null;
        var text = message.Value<string>("content") ?? string.Empty;
        var author = message["author"]?.Value<string>("id");
        if (string.IsNullOrEmpty(author)) return null;
        var label = message["member"]?.Value<string>("nick") ?? message["author"]?.Value<string>("global_name") ?? message["author"]?.Value<string>("username") ?? author;
        var source = new WebSourceDocument
        {
            provider = "discord", id = id, author = author, selftext = text,
            title = string.IsNullOrWhiteSpace(text) ? "Image attachment submission" : text.Substring(0, Math.Min(text.Length, 200)),
            url = $"https://discord.com/channels/{guild}/{channel}/{id}", fetchedAtUtc = DateTime.UtcNow.ToString("O")
        };
        var notes = new List<string>();
        var seen = new HashSet<string>();
        foreach (var attachment in message["attachments"] as JArray ?? new JArray())
        {
            var attachmentId = attachment.Value<string>("id");
            if (string.IsNullOrEmpty(attachmentId) || !seen.Add(attachmentId)) continue;
            try
            {
                if (source.images.Count >= ImageAttachmentContext.MaxImages)
                    throw new ArgumentException("image count limit reached");
                var image = DiscordIdeaSubmission.ReadImage(attachment, attachmentId);
                image.SourceUrl = source.url;
                image.SourceLabel = $"Discord submission | {label} ({author})";
                source.images.Add(image);
            }
            catch (ArgumentException e) { notes.Add($"Attachment {attachment.Value<string>("filename")}: unavailable ({e.Message})"); }
        }
        source.threadContext = string.Join("\n", notes);
        if (source.threadContext.Length > 1800) source.threadContext = source.threadContext.Substring(0, 1800);
        return string.IsNullOrWhiteSpace(text) && source.images.Count == 0 ? null : source;
    }
}
