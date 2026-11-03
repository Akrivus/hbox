using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;

public static class RedditImageAttachments
{
    public static List<ImageAttachment> Parse(JToken post)
    {
        var result = new List<ImageAttachment>();
        if (post.Value<bool?>("over_18") == true || post.Value<bool?>("spoiler") == true) return result;
        Read(post, result);
        // Crossposts often carry the actual media only on the original post.
        foreach (var parent in post["crosspost_parent_list"] as JArray ?? new JArray())
            if (result.Count == 0) Read(parent, result);
        return result;
    }

    private static void Read(JToken post, List<ImageAttachment> result)
    {
        if (post.Value<bool?>("over_18") == true || post.Value<bool?>("spoiler") == true || post.Value<bool?>("is_video") == true) return;
        var id = post.Value<string>("id") ?? "unknown";
        var permalink = post.Value<string>("permalink");
        var sourceUrl = string.IsNullOrWhiteSpace(permalink) ? null :
            permalink.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? permalink : "https://www.reddit.com" + permalink;
        void Add(string url, string key, string caption)
        {
            url = WebUtility.HtmlDecode(url);
            if (result.Count >= ImageAttachmentContext.MaxImages || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                result.Any(image => image.Url == uri.AbsoluteUri)) return;
            result.Add(new ImageAttachment { Id = "reddit:" + id + ":" + key, Url = uri.AbsoluteUri,
                SourceUrl = sourceUrl, SourceLabel = $"Reddit {post.Value<string>("subreddit_name_prefixed")} | u/{post.Value<string>("author")}", Caption = caption });
        }
        var gallery = post["gallery_data"]?["items"] as JArray;
        if (gallery != null)
        {
            foreach (var item in gallery)
            {
                var mediaId = item.Value<string>("media_id");
                var media = mediaId == null ? null : post["media_metadata"]?[mediaId];
                if (media?.Value<string>("status") != "valid" || media.Value<string>("e") != "Image") continue;
                Add(media["s"]?.Value<string>("u"), mediaId, item.Value<string>("caption"));
            }
            return;
        }
        var direct = post.Value<string>("url_overridden_by_dest") ?? post.Value<string>("url");
        var isDirect = Uri.TryCreate(WebUtility.HtmlDecode(direct), UriKind.Absolute, out var directUri) &&
            new[] { ".png", ".jpg", ".jpeg" }.Any(ext => directUri.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        if (isDirect) Add(direct, "image", post.Value<string>("title"));
        // Article thumbnails and video previews are not source photos.
        else if (post.Value<string>("post_hint") == "image")
            Add(post["preview"]?["images"]?.FirstOrDefault()?["source"]?.Value<string>("url"), "image", post.Value<string>("title"));
    }
}
