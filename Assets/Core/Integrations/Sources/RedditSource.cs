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
    public bool EnablePitchGate = false;
    public string PitchDiscordChannel = "#stream";
    public int PitchExpirationMinutes = 180;
    public int PitchAutoApprovalBatchSize = 0;
    public int PitchMinimumVotesToQueue = 1;

    private List<string> history = new List<string>();
    private Dictionary<string, DateTime> fetchTimes = new Dictionary<string, DateTime>();
    private Queue<Idea> ideas = new Queue<Idea>();
    private string fileName;
    private PitchCandidateStore pitchStore;

    private int i = 0;
    private RedditThreadMiner miner;
    private ChatManagerContext chatManagerContext;
    private RedditPitchCandidateService pitchCandidateService;

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
        EnablePitchGate = c.EnablePitchGate;
        PitchDiscordChannel = c.PitchDiscordChannel;
        PitchExpirationMinutes = Mathf.Max(1, c.PitchExpirationMinutes);
        PitchAutoApprovalBatchSize = Mathf.Max(0, c.PitchAutoApprovalBatchSize);
        PitchMinimumVotesToQueue = Mathf.Max(0, c.PitchMinimumVotesToQueue);
        RedditRequestGate.Configure(
            c.RequestSpacingSeconds,
            c.RateLimitCooldownSeconds,
            c.MaxRequestAttempts,
            c.OAuthClientId,
            c.OAuthClientSecret,
            c.OAuthDeviceId,
            c.OAuthUserAgent);

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

            UiEventBus.Publish(chatManagerContext, GetNextRunTime());
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
        var canAutoApprove = IsInActiveWindow(DateTime.Now);
        if (EnablePitchGate && canAutoApprove)
            pitchStore?.ResolveFinishedVotes(PitchMinimumVotesToQueue);

        yield return FetchIdeas(canAutoApprove).AsCoroutine();

        while (ideas.TryDequeue(out var idea))
            generator.AddIdeaToQueue(idea);

        OnBatchEnd?.Invoke();
    }

    public void DoDrop()
    {
        StartCoroutine(Drop());
    }

    public async Task FetchIdeas(bool canAutoApprove = true)
    {
        var promptTemplate = await PromptResolver.Read(generator.ManagerContext, "Reddit Source", "{0}");
        var postedPitches = 0;
        var autoApprovalsRemaining = EnablePitchGate && canAutoApprove ? PitchAutoApprovalBatchSize : 0;

        for (var iteration = 0; iteration < BatchIterations; iteration++)
            for (var iterations = 0; iterations < BatchSize; iterations++)
            {
                var subreddit = SubReddits.ElementAt(i);
                IEnumerable<JToken> range;
                try
                {
                    range = await FetchAsync(subreddit.Key);
                }
                catch (RedditRateLimitException e)
                {
                    Debug.LogWarning(e.Message);
                    return;
                }

                var value = await BuildSubPrompt(string.Format(await FindMetaPrompt("{0}"), subreddit.Value));
                var prompt = string.Format(promptTemplate, value, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                var posts = OrderPostsForPitchGate(range.Take(BatchSize)).ToList();
                foreach (var post in posts)
                {
                    var autoApprove = EnablePitchGate && autoApprovalsRemaining > 0;
                    var accepted = await PostToIdea(post, prompt, autoApprove);
                    if (accepted)
                    {
                        RememberPost(post.Value<string>("id"));
                        if (autoApprove)
                            autoApprovalsRemaining--;
                    }
                    if (accepted && EnablePitchGate)
                        postedPitches++;
                }

                i = ++i % SubReddits.Count;
                if (ideas.Count + postedPitches >= BatchSizeLimit)
                    return;
            }
    }

    private IEnumerable<JToken> OrderPostsForPitchGate(IEnumerable<JToken> posts)
    {
        if (!EnablePitchGate)
            return posts;

        return posts
            .OrderByDescending(ScoreRedditPost)
            .ThenByDescending(post => post.Value<int?>("num_comments") ?? 0)
            .ThenByDescending(post => post.Value<int?>("score") ?? 0)
            .ThenByDescending(post => post.Value<long?>("created_utc") ?? 0);
    }

    private static float ScoreRedditPost(JToken post)
    {
        var comments = Mathf.Max(0, post.Value<int?>("num_comments") ?? 0);
        var score = Mathf.Max(0, post.Value<int?>("score") ?? 0);
        return (comments * 3f) + Mathf.Sqrt(score);
    }

    private async Task<bool> PostToIdea(JToken post, string template, bool autoApprove = false)
    {
        var source = await BuildSourceAsync(post);
        var topic = source.title +
            "\n\n" + source.selftext +
            "\n\n" + source.threadContext;

        if (EnablePitchGate)
        {
            var candidate = await GeneratePitchCandidate(source, topic);
            if (candidate == null)
                return false;

            if (autoApprove)
            {
                pitchStore.QueueCandidate(candidate, "pitch_auto_queued");
                UiEventBus.Publish(chatManagerContext, $"Auto-approved Reddit pitch: {PitchCandidateText.GetTitle(candidate)}");
                return true;
            }

            PitchDiscordPublisher.Publish(candidate, pitchStore, chatManagerContext);
            UiEventBus.Publish(chatManagerContext, $"Posted Reddit pitch for vote: {PitchCandidateText.GetTitle(candidate)}");
            return true;
        }

        var idea = new Idea(
            string.Format(template, topic),
            source.author,
            source.subreddit,
            source.id
        );

        ideas.Enqueue(idea);

        return true;
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

    private void RememberPost(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || history.Contains(id))
            return;

        history.Add(id);
    }

    private async Task<PitchCandidate> GeneratePitchCandidate(RedditPostSource source, string topic)
    {
        var candidate = await pitchCandidateService.GenerateAsync(
            source,
            topic,
            ResolvePitchDiscordChannel(),
            PitchExpirationMinutes);
        pitchStore.Save(candidate);
        return candidate;
    }

    private string ResolvePitchDiscordChannel()
    {
        if (!string.IsNullOrWhiteSpace(PitchDiscordChannel) && !string.Equals(PitchDiscordChannel, "#stream", StringComparison.OrdinalIgnoreCase))
            return PitchDiscordChannel;

        return DiscordManager.GetStreamChannel(chatManagerContext);
    }

    private void Start()
    {
        chatManagerContext = ChatManagerContext.Current;
        pitchStore = new PitchCandidateStore(chatManagerContext, generator);
        pitchCandidateService = new RedditPitchCandidateService(chatManagerContext, generator);
        chatManagerContext.ConfigManager.RegisterConfig(typeof(RedditConfigs), "reddit", (_config) => Configure((RedditConfigs)_config));
    }

    private void OnDestroy()
    {
        if (fileName != null)
            File.WriteAllLines(fileName, history);
        StopAllCoroutines();
    }

    private List<string> LoadHistory()
    {
        fileName = $"reddit-{chatManagerContext.Key}.txt";
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
        using var client = new TimeoutWebClient();

        var json = RedditRequestGate.DownloadString(client, url);
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
        var nextRun = new DateTime(now.Year, now.Month, now.Day, offset.Hours, offset.Minutes, offset.Seconds);
        while (nextRun <= now) nextRun = nextRun.AddMinutes(BatchPeriodInMinutes);

        return nextRun;
    }

    private bool IsInActiveWindow(DateTime now)
    {
        return IsInWindow(
            now.TimeOfDay,
            TimeSpan.Parse(ActiveWindowStart),
            TimeSpan.Parse(ActiveWindowEnd));
    }

    private static bool IsInWindow(TimeSpan t, TimeSpan start, TimeSpan end)
    {
        if (start < end)
            return t >= start && t < end;
        if (start > end)
            return t >= start || t < end;
        return true;
    }
}
