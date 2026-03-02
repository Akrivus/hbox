using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class RedditSource : MonoBehaviour, IConfigurable<RedditConfigs>
{
    private static readonly DateTime EPOCH = new DateTime(1970, 1, 1);

    public event Action OnBatchStart;
    public event Action OnBatchEnd;

    [SerializeField]
    private ChatGenerator generator;

    public Dictionary<string, string> SubReddits = new Dictionary<string, string>();
    public float MaxPostAgeInHours = 24;
    public int BatchSize = 20;
    public int BatchSizeLimit = 20;
    public int BatchIterations = 1;
    public string BatchPeriodOffset = "00:00";
    public float BatchPeriodInMinutes = 60;

    public string ActiveWindowStart = "00:00";
    public string ActiveWindowEnd = "23:59";

    private List<string> history = new List<string>();
    private Dictionary<string, DateTime> fetchTimes = new Dictionary<string, DateTime>();
    private Queue<Idea> ideas = new Queue<Idea>();
    private string fileName;

    private int i = 0;
    private RedditThreadMiner miner;
    private ChatManagerContext chatManagerContext;

    public void Configure(RedditConfigs c)
    {
        SubReddits = c.SubReddits.Shuffle()
            .ToDictionary(k => k.Key, v => v.Value);
        MaxPostAgeInHours = c.MaxPostAgeInHours;
        BatchSize = c.BatchSize;
        BatchSizeLimit = c.BatchSizeLimit;
        BatchIterations = c.BatchIterations;
        BatchPeriodOffset = c.BatchPeriodOffset;
        BatchPeriodInMinutes = c.BatchPeriodInMinutes;
        ActiveWindowStart = c.ActiveHoursStart;
        ActiveWindowEnd = c.ActiveHoursEnd;

        i = UnityEngine.Random.Range(0, SubReddits.Count);

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

        history = LoadHistory();
        StartCoroutine(Drops());
    }

    public IEnumerator Drops()
    {
        do
        {
            yield return WhenUnpaused();

            var nextRunTime = GetNextRunTime();
            UiEventBus.Publish(chatManagerContext, nextRunTime);
            yield return new WaitUntil(() => chatManagerContext.IsActive && DateTime.Now >= nextRunTime);

            yield return Drop();
            yield return WhenUnpaused();
        } while (chatManagerContext.IsActive);
    }

    private IEnumerator WhenUnpaused()
    {
        yield return new WaitUntil(() => !ChatManager.IsPaused);
    }

    public IEnumerator Drop()
    {
        OnBatchStart?.Invoke();
        yield return FetchIdeas().AsCoroutine();

        while (ideas.TryDequeue(out var idea))
            yield return generator.GenerateAndPlay(idea).AsCoroutine();
        OnBatchEnd?.Invoke();
    }

    public async Task FetchIdeas()
    {
        var prompt = await PromptResolver.Read(generator.ManagerContext, "Reddit Source", "{0}");
        for (var iteration = 0; iteration < BatchIterations; iteration++)
            for (var iterations = 0; iterations < BatchSize; iterations++)
            {
                var subreddit = SubReddits.ElementAt(i);
                var range = await FetchAsync(subreddit.Key);
                var value = await BuildSubPrompt(string.Format(await FindMetaPrompt("{0}"), subreddit.Value));
                prompt = string.Format(prompt, value, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                var posts = range.Take(BatchSize)
                    .Select(post =>
                    {
                        history.Add(post.Value<string>("id"));
                        return post;
                    }).ToList();
                foreach (var post in posts)
                    PostToIdea(post, prompt);
                i = i++ % SubReddits.Count;
                if (ideas.Count >= BatchSizeLimit)
                    return;
            }
    }

    private Idea PostToIdea(JToken post, string template)
    {
        var permalink = post.Value<string>("permalink");
        var top = miner.Mine(permalink);
        var topic = post.Value<string>("title") +
            "\n\n" + post.Value<string>("selftext") +
            "\n\n" + string.Join("\n\n", top.Select(t => t.DialogueSeed));

        var idea = new Idea(
            string.Format(template, topic),
            post.Value<string>("author"),
            post.Value<string>("subreddit_name_prefixed"),
            post.Value<string>("id")
        );

        ideas.Enqueue(idea);

        return idea;
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(RedditConfigs), "reddit", (_config) => Configure((RedditConfigs)_config));
        chatManagerContext = ChatManagerContext.Current;
    }

    private void OnDestroy()
    {
        if (fileName != null)
            File.WriteAllLines(fileName, history);
        StopAllCoroutines();
    }

    private List<string> LoadHistory()
    {
        fileName = $"reddit-{ChatManagerContext.Current.Key}.txt";
        if (!File.Exists(fileName))
            return new List<string>();
        return File.ReadAllLines(fileName).ToList();
    }

    public Task<IEnumerable<JToken>> FetchAsync(string subreddit, int batchSize = 0)
    {
        return Task.Run(() => Fetch(subreddit, batchSize));
    }

    public IEnumerable<JToken> Fetch(string uri, int batchSize = 0)
    {
        var fetchTime = fetchTimes.GetValueOrDefault(uri, DateTime.Now.AddHours(-MaxPostAgeInHours));
        var cutoff = fetchTime.Subtract(EPOCH).TotalSeconds;

        var parts = uri.Split(new char[] { '?' }, StringSplitOptions.RemoveEmptyEntries);
        var subreddit = parts[0];
        var query = parts.Length > 1 ? parts[1] : null;
        var url = $"https://www.reddit.com/r/{subreddit}.json?{query}";
        var client = new WebClient();

        client.Headers.Add("User-Agent", "polbot:1.0 (by /u/Akrivus)");

        var json = client.DownloadString(url);
        var data = JObject.Parse(json);

        fetchTimes[subreddit] = DateTime.Now;

        if (batchSize <= 0)
            batchSize = BatchSize;

        return data.SelectTokens("$.data.children[*].data")
            .Where(post => post.Value<long>("created_utc") > cutoff)
            .Where(post => !history.Contains(post.Value<string>("id")))
            .OrderByDescending(post => post.Value<long>("created_utc"))
            .Take(batchSize);
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

    private DateTime GetNextRunTime()
    {
        var now = DateTime.Now;

        var offset = TimeSpan.Parse(BatchPeriodOffset);
        var windowStart = TimeSpan.Parse(ActiveWindowStart);
        var windowEnd = TimeSpan.Parse(ActiveWindowEnd);

        var nextRun = new DateTime(now.Year, now.Month, now.Day, offset.Hours, offset.Minutes, offset.Seconds);
        while (nextRun <= now) nextRun = nextRun.AddMinutes(BatchPeriodInMinutes);

        if (!IsInWindow(nextRun.TimeOfDay, windowStart, windowEnd))
        {
            nextRun = NextWindowStart(now, windowStart, windowEnd);
            var baseSlot = new DateTime(nextRun.Year, nextRun.Month, nextRun.Day, offset.Hours, offset.Minutes, offset.Seconds);
            if (baseSlot < nextRun) baseSlot = nextRun;
            nextRun = baseSlot;
            while (nextRun <= now)
                nextRun = nextRun.AddMinutes(BatchPeriodInMinutes);
            if (!IsInWindow(nextRun.TimeOfDay, windowStart, windowEnd))
                nextRun = NextWindowStart(nextRun, windowStart, windowEnd);
        }
        return nextRun;
    }

    private static bool IsInWindow(TimeSpan t, TimeSpan start, TimeSpan end)
    {
        if (start < end)
            return t >= start && t < end;
        if (start > end)
            return t >= start || t < end;
        return true;
    }

    private static DateTime NextWindowStart(DateTime now, TimeSpan start, TimeSpan end)
    {
        var todayStart = now.Date + start;
        var todayEnd = now.Date + end;

        if (start < end)
        {
            if (now < todayStart) return todayStart;
            if (now >= todayEnd) return now.Date.AddDays(1) + start;
            return now;
        }
        else if (start > end)
        {
            if (now.TimeOfDay < end) return now;
            if (now < todayStart) return todayStart;
            return now;
        }
        return now;
    }

}