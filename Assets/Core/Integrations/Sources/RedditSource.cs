using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class RedditSource : WebSource, IConfigurable<RedditConfigs>
{
    private static readonly DateTime EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public override string SourceKey => "reddit";
    public override string PromptRoot => "Reddit Source";
    public Dictionary<string, string> SubReddits = new Dictionary<string, string>();
    public float MaxPostAgeInHours = 24;
    private List<string> history = new List<string>();
    private RedditThreadMiner miner;
    private string fileName;
    private int i;
    private bool rankForPitch;

    protected override void RegisterConfig()
    {
        chatManagerContext.ConfigManager.RegisterConfig(typeof(RedditConfigs), "reddit", value => Configure((RedditConfigs)value));
    }

    public void Configure(RedditConfigs c)
    {
        SubReddits = (c.SubReddits ?? new Dictionary<string, string>()).Shuffle().ToDictionary(k => k.Key, v => v.Value);
        MaxPostAgeInHours = c.MaxPostAgeInHours;
        rankForPitch = c.EnablePitchGate;
        RedditRequestGate.Configure(
            c.RequestSpacingSeconds,
            c.RateLimitCooldownSeconds,
            c.MaxRequestAttempts,
            c.OAuthClientId,
            c.OAuthClientSecret,
            c.OAuthDeviceId,
            c.OAuthUserAgent);

        i = UnityEngine.Random.Range(0, Math.Max(1, SubReddits.Count));

        miner = new RedditThreadMiner
        {
            MaxDepth = c.MaxDepth,
            TopRoots = c.TopRoots,
            TopLevelLimit = c.TopLevelLimit,
            PerLevelChildLimit = c.PerLevelChildLimit,
            MaxDialogueLines = c.MaxDialogueLines,
            MaxCharsPerLine = c.MaxCharsPerLine,
            Sort = c.Sort
        };

        fileName = $"reddit-{chatManagerContext.Key}.txt";
        history = File.Exists(fileName) ? File.ReadAllLines(fileName).ToList() : new List<string>();
        ConfigureWebSource(c);
    }

    public override async Task<IReadOnlyList<WebSourceDocument>> FetchBatchAsync(int limit)
    {
        if (SubReddits.Count == 0 || limit <= 0) return Array.Empty<WebSourceDocument>();
        var subreddit = SubReddits.ElementAt(i);
        i = (i + 1) % SubReddits.Count;
        var posts = await FetchAsync(subreddit.Key, limit);
        if (rankForPitch)
            posts = posts.OrderByDescending(post => Math.Max(0, post.Value<int?>("num_comments") ?? 0) * 3d +
                Math.Sqrt(Math.Max(0, post.Value<int?>("score") ?? 0)))
                .ThenByDescending(post => post.Value<long?>("created_utc") ?? 0);
        var context = await BuildSubPrompt(string.Format(await FindMetaPrompt("{0}"), subreddit.Value));
        var result = new List<WebSourceDocument>();
        foreach (var post in posts)
        {
            var source = await BuildSourceAsync(post);
            source.promptContext = context;
            source.fetchedAtUtc = DateTime.UtcNow.ToString("O");
            result.Add(source);
        }
        return result;
    }

    public override void RememberAccepted(WebSourceDocument source)
    {
        if (string.IsNullOrWhiteSpace(source.id) || history.Contains(source.id)) return;
        history.Add(source.id);
        // Persist after acceptance, rather than losing a whole session's history on a crash.
        if (fileName != null) File.WriteAllLines(fileName, history);
    }

    private async Task<RedditPostSource> BuildSourceAsync(JToken post)
    {
        var permalink = post.Value<string>("permalink");
        var id = post.Value<string>("id");
        var title = post.Value<string>("title");
        var selftext = post.Value<string>("selftext");
        var author = post.Value<string>("author");
        var subreddit = post.Value<string>("subreddit_name_prefixed");
        var url = post.Value<string>("url");
        var score = post.Value<int?>("score") ?? 0;
        var commentCount = post.Value<int?>("num_comments") ?? 0;
        var top = await Task.Run(() =>
        {
            try
            {
                return miner.Mine(permalink);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Reddit thread mining failed for {permalink}: {e.Message}");
                return new List<RedditThreadMiner.ThreadPick>();
            }
        });

        return new RedditPostSource
        {
            id = id,
            title = title,
            selftext = selftext,
            author = author,
            subreddit = subreddit,
            permalink = permalink,
            url = url,
            score = score,
            commentCount = commentCount,
            threadContext = string.Join("\n\n", top.Select(t => t.DialogueSeed))
        };
    }

    public Task<IEnumerable<JToken>> FetchAsync(string subreddit, int batchSize = 0)
    {
        return Task.Run(() => Fetch(subreddit, batchSize));
    }

    public IEnumerable<JToken> Fetch(string uri, int batchSize = 0)
    {
        // Seen history suppresses accepted posts; rejected posts remain eligible within the age window.
        var cutoff = DateTime.UtcNow.AddHours(-MaxPostAgeInHours).Subtract(EPOCH).TotalSeconds;

        var parts = uri.Split(new char[] { '?' }, StringSplitOptions.RemoveEmptyEntries);
        var subreddit = parts[0];
        var query = parts.Length > 1 ? parts[1] : null;
        var url = $"https://www.reddit.com/r/{subreddit}.json?{query}";
        using var client = new TimeoutWebClient();

        var json = RedditRequestGate.DownloadString(client, url);
        var data = JObject.Parse(json);

        if (batchSize <= 0)
            batchSize = 20;

        return data.SelectTokens("$.data.children[*].data")
            .Where(post => post.Value<long>("created_utc") > cutoff)
            .Where(post => !history.Contains(post.Value<string>("id")))
            .OrderByDescending(post => post.Value<long>("created_utc"))
            .Take(batchSize)
            .ToList();
    }

    private async Task<string> FindMetaPrompt(string blank = null)
    {
        var names = new string[]
        {
            DateTime.Now.ToString("MMMM d"),
            DateTime.Now.ToString("MMMM"),
            DateTime.Now.ToString("dddd"),
            DateTime.Now.ToString("HH"),
            "Default"
        };
        foreach (var name in names)
        {
            var prompt = await PromptResolver.Read(generator.ManagerContext, "Reddit Source/" + name);
            if (prompt == null) continue;
            if (!prompt.Contains("{0}"))
                prompt += "\n\n{0}";
            return prompt;
        }
        return blank;
    }

    private async Task<string> BuildSubPrompt(string text)
    {
        if (text.StartsWith("./"))
            text = await PromptResolver.Read(generator.ManagerContext, text, "{0}");
        if (!text.Contains("{0}"))
            text += "\n\n{0}";
        return text;
    }

}
