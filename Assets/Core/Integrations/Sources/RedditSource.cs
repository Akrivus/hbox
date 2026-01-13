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
    public static RedditSource Instance { get; private set; }

    private static readonly DateTime EPOCH = new DateTime(1970, 1, 1);

    public event Action OnBatchStart;
    public event Action OnBatchEnd;

    [SerializeField]
    private ChatGenerator generator;

    public Dictionary<string, string> SubReddits = new Dictionary<string, string>();
    public float MaxPostAgeInHours = 24;
    public int BatchMax = 20;
    public float BatchPeriodInMinutes = 60;

    private List<string> history = new List<string>();
    private Dictionary<string, DateTime> fetchTimes = new Dictionary<string, DateTime>();
    private Queue<Task<Idea>> ideas = new Queue<Task<Idea>>();

    private int i = 0;
    private RedditThreadMiner miner;

    public void Configure(RedditConfigs c)
    {
        SubReddits = c.SubReddits.Shuffle()
            .ToDictionary(k => k.Key, v => v.Value);
        MaxPostAgeInHours = c.MaxPostAgeInHours;
        BatchMax = c.BatchMax;
        BatchPeriodInMinutes = c.BatchPeriodInMinutes;

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

    public void TriggerDrop()
    {
        StartCoroutine(Drop());
    }

    public IEnumerator Drops()
    {
        while (Application.isPlaying)
        {
            yield return new WaitUntil(() => !ChatManager.IsPaused && ChatManagerContext.Current != null);
            yield return Drop();

            yield return new WaitForSeconds(BatchPeriodInMinutes * 60);
        }
    }

    public IEnumerator Drop()
    {
        OnBatchStart?.Invoke();
        yield return FetchIdeas().AsCoroutine();

        while (ideas.TryDequeue(out var task))
        {
            yield return task.AsCoroutine();
            yield return generator.GenerateAndPlay(task.Result).AsCoroutine();
        }
        OnBatchEnd?.Invoke();
    }

    public async Task FetchIdeas()
    {
        var prompt = new PromptResolver("Reddit Source");
        var metaprompt = FindMetaPrompt();

        for (var _ = i; _ < SubReddits.Count; _++)
        {
            var subreddit = SubReddits.ElementAt(_);
            var range = await FetchAsync(subreddit.Key);
            var subprompt = await BuildSubPrompt(metaprompt, subreddit.Value);
            ideas = new Queue<Task<Idea>>(range
                    .Take(BatchMax)
                    .Select(post =>
                    {
                        history.Add(post.Value<string>("id"));
                        return post;
                    })
                    .Select(post => PostToIdea(post).RePrompt(prompt, subprompt)
                ).ToList());
            if (ideas.Count >= BatchMax || !Application.isPlaying)
                break;
            i = _;
        }
        if (i >= SubReddits.Count - 1)
            i = 0;
    }

    private Idea PostToIdea(JToken post)
    {
        var permalink = post.Value<string>("permalink");
        var top = miner.Mine(permalink);
        var topic = post.Value<string>("selftext") + "\n\n" + string.Join("\n\n", top.Select(t => t.DialogueSeed));

        return new Idea(
            post.Value<string>("title"),
            topic,
            post.Value<string>("author"),
            post.Value<string>("subreddit_name_prefixed"),
            post.Value<string>("id")
        );
    }

    private void Awake()
    {
        Instance = this;
        ConfigManager.Instance.RegisterConfig(typeof(RedditConfigs), "reddit", (config) => Configure((RedditConfigs)config));
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        File.WriteAllLines("reddit.txt", history);
    }

    private List<string> LoadHistory()
    {
        if (!File.Exists("reddit.txt"))
            return new List<string>();
        return File.ReadAllLines("reddit.txt").ToList();
    }

    public Task<IEnumerable<JToken>> FetchAsync(string subreddit, int batchMax = 0)
    {
        return Task.Run(() => Fetch(subreddit, batchMax));
    }

    public IEnumerable<JToken> Fetch(string subreddit, int batchMax = 0)
    {
        var fetchTime = fetchTimes.GetValueOrDefault(subreddit, DateTime.Now.AddHours(-MaxPostAgeInHours));
        var cutoff = fetchTime.Subtract(EPOCH).TotalSeconds;

        var url = $"https://www.reddit.com/r/{subreddit}.json";
        var client = new WebClient();

        client.Headers.Add("User-Agent", "polbot:1.0 (by /u/Akrivus)");

        var json = client.DownloadString(url);
        var data = JObject.Parse(json);

        fetchTimes[subreddit] = DateTime.Now;

        if (batchMax <= 0)
            batchMax = BatchMax;

        return data.SelectTokens("$.data.children[*].data")
            .Where(post => post.Value<long>("created_utc") > cutoff)
            .Where(post => !history.Contains(post.Value<string>("id")))
            .OrderByDescending(post => post.Value<long>("created_utc"))
            .Take(batchMax);
    }

    private PromptResolver FindMetaPrompt()
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
            if (PromptResolver.TryFind("Reddit Source/" + name, out var prompt))
                return prompt;
        }
        return null;
    }

    private async Task<string> BuildSubPrompt(PromptResolver prompt, string text)
    {
        if (text.StartsWith("./"))
            text = await PromptResolver.Read(text);
        if (prompt != null)
            await prompt.Resolve(text);
        return prompt != null ? prompt.Text : text;
    }
}