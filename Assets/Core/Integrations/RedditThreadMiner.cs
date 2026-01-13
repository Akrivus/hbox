using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

public class RedditThreadMiner
{
    public class CommentNode
    {
        public string Id;
        public string Author;
        public string Body;
        public int Score;
        public List<CommentNode> Children = new List<CommentNode>();
    }

    public class ThreadPick
    {
        public CommentNode Root;
        public string DialogueSeed;
    }

    public int MaxDepth = 3;
    public int TopRoots = 3;
    public int TopLevelLimit = 30;
    public int PerLevelChildLimit = 20;
    public int MaxDialogueLines = 16;
    public int MaxCharsPerLine = 280;
    public string Sort = "confidence";

    public List<ThreadPick> Mine(string permalink)
    {
        var roots = FetchForest(permalink, Sort, MaxDepth, TopLevelLimit, PerLevelChildLimit);

        var topRoots = roots
            .OrderByDescending(r => r.Score)
            .Take(Math.Max(1, TopRoots))
            .ToList();

        var picks = new List<ThreadPick>();
        foreach (var root in topRoots)
        {
            picks.Add(new ThreadPick
            {
                Root = root,
                DialogueSeed = FormatDialogue(root, MaxDialogueLines, MaxCharsPerLine)
            });
        }

        return picks;
    }

    private static List<CommentNode> FetchForest(string permalink, string sort, int maxDepth, int topLimit, int perLevelLimit)
    {
        if (!permalink.EndsWith("/")) permalink += "/";

        var url = $"https://www.reddit.com{permalink}.json?raw_json=1&sort={sort}&limit=100";

        using var client = NewClient();
        var json = client.DownloadString(url);
        var arr = JArray.Parse(json);
        if (arr.Count < 2) return new List<CommentNode>();

        var commentsListing = arr[1]["data"]?["children"] as JArray;
        if (commentsListing == null) return new List<CommentNode>();

        var roots = new List<CommentNode>();
        int count = 0;

        foreach (var child in commentsListing)
        {
            if (child?["kind"]?.ToString() != "t1") continue;

            var data = child["data"];
            var root = ParseComment(data, 0, maxDepth, perLevelLimit);
            if (root != null)
            {
                roots.Add(root);
                count++;
                if (count >= topLimit) break;
            }
        }

        return roots;
    }

    private static CommentNode ParseComment(JToken data, int depth, int maxDepth, int perLevelChildLimit)
    {
        if (data == null) return null;

        var author = data.Value<string>("author");
        if (string.IsNullOrWhiteSpace(author) || author == "[deleted]" || author == "AutoModerator")
            return null;
        var body = data.Value<string>("body");
        if (string.IsNullOrWhiteSpace(body) || body == "[deleted]" || body == "[removed]")
            return null;

        var node = new CommentNode
        {
            Id = data.Value<string>("id"),
            Author = author,
            Body = body,
            Score = data.Value<int?>("score") ?? 0
        };

        if (depth >= maxDepth)
            return node;

        var repliesToken = data["replies"];
        if (repliesToken == null || repliesToken.Type != JTokenType.Object)
            return node;

        var repliesData = repliesToken["data"]?["children"] as JArray;
        if (repliesData == null) return node;

        int added = 0;
        foreach (var child in repliesData)
        {
            if (child?["kind"]?.ToString() != "t1") continue;

            var childNode = ParseComment(child["data"], depth + 1, maxDepth, perLevelChildLimit);
            if (childNode != null)
            {
                node.Children.Add(childNode);
                added++;
                if (added >= perLevelChildLimit) break;
            }
        }

        return node;
    }

    private static WebClient NewClient()
    {
        var _ = new WebClient();
        _.Headers.Add("User-Agent", "polbot:1.0 (by /u/Akrivus)");
        return _;
    }

    public string FormatDialogue(CommentNode root, int maxLines, int maxCharsPerLine)
    {
        var sb = new StringBuilder();
        int lines = 0;

        void Emit(CommentNode n, int depth)
        {
            if (lines >= maxLines) return;

            var author = string.IsNullOrEmpty(n.Author) ? "anon" : n.Author;
            var text = Condense(n.Body, maxCharsPerLine);

            int indent = Math.Min(depth, 4) * 2;
            sb.Append(' ', indent);
            sb.Append('[').Append(author).Append("]: ").AppendLine(text);
            lines++;

            if (lines >= maxLines) return;

            foreach (var c in n.Children)
            {
                if (lines >= maxLines) break;
                Emit(c, depth + 1);
            }
        }

        Emit(root, 0);
        return sb.ToString().TrimEnd();
    }

    private static string Condense(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.Replace("\r", " ").Replace("\n", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
        if (t.Length <= max) return t;
        return t.Substring(0, Math.Max(0, max - 1)) + "…";
    }
}
