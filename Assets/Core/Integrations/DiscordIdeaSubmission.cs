using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

// Converts a slash-command submission to the shared Idea used by generation/replays.
public static class DiscordIdeaSubmission
{
    public static readonly string[] ImageOptionNames = { "image", "image2", "image3", "image4" };

    public static Idea FromInteraction(JToken payload, string prompt)
    {
        var user = payload?["member"]?["user"] ?? payload?["user"];
        var userId = user?.Value<string>("id") ?? "unknown";
        var interactionId = payload?.Value<string>("id");
        var idea = new Idea(prompt, userId, "discord", string.IsNullOrWhiteSpace(interactionId) ? null : "discord:" + interactionId);
        var options = payload?["data"]?["options"] as JArray ?? new JArray();
        var resolved = payload?["data"]?["resolved"]?["attachments"] as JObject;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var displayName = payload?["member"]?.Value<string>("nick") ?? user?.Value<string>("global_name") ?? user?.Value<string>("username") ?? userId;
        var guildId = payload?.Value<string>("guild_id") ?? "@me";
        var channelId = payload?.Value<string>("channel_id");
        foreach (var name in ImageOptionNames)
        {
            var option = options.FirstOrDefault(value => value.Value<string>("name") == name);
            if (option == null) continue;
            var id = option.Value<string>("value");
            var attachment = string.IsNullOrWhiteSpace(id) ? null : resolved?[id];
            if (option.Value<int?>("type") != 11 || attachment == null)
                throw new ArgumentException($"The `{name}` upload could not be resolved. Please attach it again.");
            if (!seen.Add(id)) continue;
            var image = ReadImage(attachment, id);
            image.SourceUrl = string.IsNullOrEmpty(channelId) ? null : $"https://discord.com/channels/{guildId}/{channelId}";
            image.SourceLabel = $"Discord /idea | {displayName} ({userId})";
            idea.Images.Add(image);
        }
        if (options.Any(option => option.Value<string>("name") == "context_only" && option.Value<bool?>("value") == true))
            foreach (var image in idea.Images) image.ContextOnly = true;
        return idea;
    }

    internal static ImageAttachment ReadImage(JToken attachment, string id)
    {
        var fileName = attachment.Value<string>("filename") ?? "image";
        var size = attachment.Value<long?>("size");
        var mime = attachment.Value<string>("content_type")?.Split(';')[0].Trim().ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (size == null || size <= 0 || size > ImageAttachmentContext.MaxImageBytes)
            throw new ArgumentException("Each image must be at most 2 MiB.");
        if ((mime != null && mime != "image/png" && mime != "image/jpeg") ||
            (mime == null && extension != ".png" && extension != ".jpg" && extension != ".jpeg"))
            throw new ArgumentException("Image uploads must be PNG or JPEG files.");
        var url = attachment.Value<string>("url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            (uri.Host != "cdn.discordapp.com" && uri.Host != "media.discordapp.net") ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443)
            throw new ArgumentException("The upload did not contain a valid Discord image URL. Please attach it again.");
        return new ImageAttachment
        {
            Id = "discord:attachment:" + id,
            Url = url,
            Caption = string.IsNullOrWhiteSpace(attachment.Value<string>("description")) ? fileName : fileName + " | " + attachment.Value<string>("description")
        };
    }
}
