using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class Tests
{
    private static int checks;
    static async Task Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var sandbox = Path.Combine(root, "Temp", "WebSourceTests", "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        Directory.SetCurrentDirectory(sandbox);
        try
        {
            ConfigAndSchedule();
            var document = Parsing();
            if (args.Length == 1)
            {
                var events = JArray.Parse(File.ReadAllText(args[0]));
                Check(events.OfType<JObject>().Any(item => PolymarketSource.ParseEvent(item, new PolymarketConfigs(), DateTime.UtcNow) != null), "live Gamma response parses eligible market");
            }
            VotingAndCompatibility(document);
            await Batching(document);
            Console.WriteLine($"Web source tests passed: {checks} checks (parsing, schedules, legacy persistence, mixed voting, branding, batching and reloads).");
        }
        finally { Directory.SetCurrentDirectory(root); }
    }

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }

    static ChatManagerContext Context(string key = null)
    {
        var context = new ChatManagerContext();
        if (key != null) context.Key = key;
        context.Generator = new ChatGenerator { ManagerContext = context };
        ChatManagerContext.Current = context;
        return context;
    }

    static void ConfigAndSchedule()
    {
        var legacy = JsonConvert.DeserializeObject<RedditConfigs>("{\"BatchSize\":2,\"BatchSizeLimit\":3,\"BatchPeriodInMinutes\":5,\"SubReddits\":{\"worldnews/top\":\"{0}\"}}");
        Check(legacy.Batch == null && legacy.BatchSize == 2 && legacy.BatchSizeLimit == 3, "legacy flat Reddit config");
        var nested = JsonConvert.DeserializeObject<PolymarketConfigs>("{\"BatchSize\":99,\"Batch\":{\"BatchSize\":3,\"BatchPeriodInMinutes\":15}}");
        Check(nested.Batch.BatchSize == 3 && nested.Batch.BatchPeriodInMinutes == 15, "separate nested batch config");
        var schedule = new WebSourceBatchConfigs { BatchPeriodOffset = "00:02", BatchPeriodInMinutes = 5, ActiveHoursStart = "22:00", ActiveHoursEnd = "06:00" };
        var now = new DateTime(2026, 10, 20, 12, 2, 0);
        Check(schedule.NextRun(now) == now.AddMinutes(5), "strictly future aligned schedule");
        Check(schedule.IsActive(now.Date.AddHours(23)) && schedule.IsActive(now.Date.AddHours(5)) && !schedule.IsActive(now), "overnight active window");
        schedule.BatchPeriodInMinutes = 0;
        try { schedule.Validate(); Check(false, "invalid schedule must fail"); }
        catch (ArgumentException) { checks++; }
        schedule.BatchPeriodInMinutes = float.NaN;
        try { schedule.Validate(); Check(false, "NaN schedule must fail"); }
        catch (ArgumentException) { checks++; }
        Check(WebSourcePrompts.Format("{0}\n{1}", "literal {1}", "date") == "literal {1}\ndate", "source braces stay literal");
    }

    static JObject Event() => JObject.Parse(@"{
      'id':'42','slug':'test-event','title':'An event','active':true,'closed':false,
      'markets':[
        {'id':'bad','question':'Closed?','active':true,'closed':true,'volume24hr':99999},
        {'id':'thin','question':'Thin?','liquidityNum':2,'volume24hr':99999},
        {'id':'good','question':'Will it happen?','active':true,'closed':false,
         'description':'Resolve YES only if the official notice is published. Literal {1}.',
         'resolutionSource':'Official notice','endDate':'2026-12-31T00:00:00Z',
         'outcomes':'[""Yes"",""No""]','outcomePrices':'[""0.65"",""0.35""]',
         'liquidityNum':5000,'volume24hr':8000}
      ]}");

    static WebSourceDocument Parsing()
    {
        var config = new PolymarketConfigs();
        var snapshot = PolymarketSource.ParseEvent(Event(), config, DateTime.UtcNow);
        Check(snapshot != null && snapshot.provider == "polymarket" && snapshot.id == "42", "parse Gamma string arrays");
        Check(snapshot.market.marketId == "good" && snapshot.market.prices[0] == .65 && snapshot.market.resolutionSource == "Official notice", "eligible market selection and rules");
        Check(snapshot.Summary().Contains("65.0") && snapshot.Summary().Contains("Snapshot:"), "snapshot prices and timestamp");
        var item = Event();
        item["markets"][2]["outcomes"] = new JArray("Yes", "No");
        item["markets"][2]["outcomePrices"] = new JArray(.6, .4);
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) != null, "native arrays supported");
        item["markets"][2]["outcomePrices"] = new JArray("NaN", ".4");
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) == null, "nonfinite prices rejected");
        item["markets"][2]["outcomePrices"] = "broken JSON";
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) == null, "malformed arrays rejected");
        item = Event(); item["closed"] = true;
        Check(PolymarketSource.ParseEvent(item, config, DateTime.UtcNow) == null, "closed event rejected");
        Check(PolymarketSource.BuildEventsUrl(config, "foreign policy", 1).Contains("tag_slug=foreign%20policy") &&
            PolymarketSource.BuildEventsUrl(config, "politics", 1).Contains("offset=50"), "escaped topic and pagination");
        return snapshot;
    }

    static void VotingAndCompatibility(WebSourceDocument document)
    {
        var context = Context();
        var store = PitchCandidateStore.GetOrCreate(context, context.Generator);
        Check(ReferenceEquals(store, PitchCandidateStore.GetOrCreate(context, context.Generator)), "adapters share one store");
        var reddit = PitchCandidateFactory.FromText("Title: Reddit card\nPitch: A receipt\nCast: France", new RedditPostSource { id = "42", title = "Post", subreddit = "r/test", permalink = "/r/test/42", selftext = "Raw post", threadContext = "Dialogue" }, "generator", "#stream", 180);
        var poly = PitchCandidateFactory.FromText("Title: Market card\nPitch: Another receipt\nCast: Germany", document, "generator", "#stream", 180);
        Check(reddit.id != poly.id, "provider IDs cannot collide");
        reddit.status = poly.status = PitchStatus.PostedForVote;
        reddit.minimumVotesToQueue = 1; reddit.approvalScore = 2;
        poly.minimumVotesToQueue = 3; poly.approvalScore = 1;
        store.Save(reddit); store.Save(poly);
        PitchCandidateStore.ApplyVote(context.Key, reddit.id, 1, 0, "test");
        PitchCandidateStore.ApplyVote(context.Key, poly.id, 1, 0, "test");
        Check(context.Generator.Ideas.Count == 0, "each source enforces its thresholds");
        PitchCandidateStore.ApplyVote(context.Key, reddit.id, 1, 0, "test");
        PitchCandidateStore.ApplyVote(context.Key, poly.id, 2, 0, "test");
        Check(context.Generator.Ideas.Count == 2 && reddit.status == PitchStatus.QueuedAsIdea && poly.status == PitchStatus.QueuedAsIdea, "mixed-source approval queues both");
        PitchCandidateStore.ApplyVote(context.Key, poly.id, 9, 0, "test");
        store.QueueCandidate(poly);
        Check(context.Generator.Ideas.Count == 2, "no duplicate delivery after approval");
        var idea = context.Generator.Ideas.Single(value => value.Source == "polymarket");
        Check(idea.Prompt.Contains("65.0") && idea.Prompt.Contains("official notice") && idea.Prompt.Contains("not facts"), "market rules and uncertainty survive conversion");
        var redditEmbed = PitchDiscordPublisher.BuildEmbed(reddit);
        var polyEmbed = PitchDiscordPublisher.BuildEmbed(poly);
        Check(redditEmbed.Author.Name == "From Reddit" && polyEmbed.Author.Name == "From Polymarket", "embed attribution");
        Check(redditEmbed.Color == 0xFF4500 && polyEmbed.Color == 0x2E5CFF && redditEmbed.Author.Icon != polyEmbed.Author.Icon, "brand colors and logos");
        Check(!polyEmbed.Description.Contains("karma") && polyEmbed.Description.Contains("24h volume"), "provider-specific embed metrics");
        var persisted = JsonConvert.DeserializeObject<PitchCandidate>(JsonConvert.SerializeObject(poly));
        Check(persisted.minimumVotesToQueue == 3 && persisted.source.market.prices[0] == .65, "policy and snapshot persist");
        var secondWindow = PitchCandidateFactory.FromText("Title: New snapshot", document, "generator", "#stream", 180);
        Check(secondWindow.id != poly.id, "repeat event gets a distinct voting window");
        var restarted = Context(context.Key);
        var restartedStore = PitchCandidateStore.GetOrCreate(restarted, restarted.Generator);
        var reloaded = PitchCandidateStore.GetPitchStatus(restarted.Key).Single(value => value.id == poly.id);
        Check(!ReferenceEquals(store, restartedStore) && reloaded.minimumVotesToQueue == 3 && reloaded.approvalScore == 1, "restart retains independent vote policies");

        var anotherShow = Context();
        var anotherStore = PitchCandidateStore.GetOrCreate(anotherShow, anotherShow.Generator);
        var repeatedReddit = PitchCandidateFactory.FromText("Title: Other show", reddit.source, "generator", "#stream", 180);
        anotherStore.Save(repeatedReddit);
        Check(PitchCandidateStore.GetPitchStatus(anotherShow.Key).Any(value => value.id == repeatedReddit.id), "same Reddit ID remains visible in another show");

        var oldContext = Context();
        File.WriteAllText($"reddit-pitches-{oldContext.Key}.json", @"{'candidates':[{'id':'old','source':{'id':'old','title':'Legacy','subreddit':'r/legacy','permalink':'/r/legacy/old'},'status':1,'expiresAtUtc':'2099-01-01T00:00:00Z'}]}");
        var oldStore = PitchCandidateStore.GetOrCreate(oldContext, oldContext.Generator);
        oldStore.ConfigureVoting(1, 2);
        var old = PitchCandidateStore.GetPitchStatus(oldContext.Key).Single();
        Check(old.source.Provider == "reddit" && old.id == "old" && old.source.SourceUrl.Contains("reddit.com/r/legacy"), "legacy source shape and ID preserved");
        PitchCandidateStore.ApplyVote(oldContext.Key, "old", 2, 0, "test");
        Check(oldContext.Generator.Ideas.Count == 1, "legacy pitches still vote and queue");

        var expired = PitchCandidateFactory.FromText("Title: Expired", document, "generator", "#stream", 180);
        expired.id = "expired-policy"; expired.status = PitchStatus.PostedForVote;
        expired.minimumVotesToQueue = 3; expired.approvalScore = 1;
        expired.upVotes = 2; expired.expiresAtUtc = DateTime.UtcNow.AddMinutes(-1).ToString("O");
        store.Save(expired); store.ResolveFinishedVotes(0);
        Check(expired.status == PitchStatus.Expired, "expiration uses per-pitch vote minimum");
    }

    static async Task Batching(WebSourceDocument document)
    {
        var context = Context();
        var source = new FakeSource(); source.Boot();
        source.Configure(new PolymarketConfigs { Batch = new WebSourceBatchConfigs { BatchSize = 10, BatchSizeLimit = 2, BatchIterations = 2 }, EnablePitchGate = true });
        var before = DiscordManager.Posted;
        await source.FetchIdeas();
        Check(source.Accepted == 2 && DiscordManager.Posted - before == 2, "strict limit inside fetched batch");
        Check(context.Generator.Ideas.Count == 0, "zero autoapproval means every candidate gets a vote");

        source.Configure(new PolymarketConfigs { BatchSize = 2, BatchSizeLimit = 1, PitchAutoApprovalBatchSize = 1 });
        await source.FetchIdeas(false);
        Check(context.Generator.Ideas.Count == 0, "outside active hours keeps autoapproval off");
        await source.FetchIdeas(true);
        Check(context.Generator.Ideas.Count == 1, "active autoapproval budget");

        LLM.Block = new TaskCompletionSource<bool>();
        var running = source.FetchIdeas();
        var fetches = source.Fetches;
        await source.FetchIdeas();
        Check(source.Fetches == fetches && source.IsBatchRunning, "concurrent manual drop ignored");
        source.Configure(new PolymarketConfigs { Enabled = false });
        LLM.Block.SetResult(true); await running; LLM.Block = null;
        Check(!source.IsBatchRunning && context.Generator.Ideas.Count == 1, "reload cancels delivery from in-flight generation");

        source.Configure(new PolymarketConfigs { BatchSize = 2, BatchSizeLimit = 2 });
        LLM.Reject = true; var acceptedBefore = source.Accepted;
        await source.FetchIdeas(); LLM.Reject = false;
        Check(source.Accepted == acceptedBefore, "evaluator rejection never enters seen history");
        source.Configure(new PolymarketConfigs { EnablePitchGate = false, BatchSize = 2, BatchSizeLimit = 1 });
        await source.FetchIdeas();
        Check(context.Generator.Ideas.Last().Prompt.Contains("Literal {1}") && context.Generator.Ideas.Last().Source == "polymarket", "direct Idea path retains provenance and source literals");

        var poly = new PolymarketSource();
        typeof(WebSource).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(poly, null);
        poly.Configure(new PolymarketConfigs()); poly.RememberAccepted(document);
        Check(File.ReadAllText($"polymarket-{context.Key}.json").Contains("42"), "Polymarket accepted event history persists");
        Check(!UnityEngine.Debug.Warnings.Any(), "batch execution has no swallowed failures: " + string.Join("; ", UnityEngine.Debug.Warnings));
    }
}

public class FakeSource : WebSource
{
    public override string SourceKey => "polymarket";
    public override string PromptRoot => "Polymarket Source";
    public int Accepted, Fetches;
    private int sequence;
    public void Boot() => Start();
    public void Configure(PolymarketConfigs config) => ConfigureWebSource(config);
    protected override void RegisterConfig() { }
    public override Task<IReadOnlyList<WebSourceDocument>> FetchBatchAsync(int limit)
    {
        Fetches++;
        return Task.FromResult<IReadOnlyList<WebSourceDocument>>(Enumerable.Range(0, limit).Select(_ => new WebSourceDocument
        { provider = "polymarket", id = (++sequence).ToString(), title = "Question", selftext = "Literal {1}", market = new PolymarketSnapshot { outcomes = new[] { "Yes", "No" }, prices = new[] { .6, .4 } } }).ToList());
    }
    public override void RememberAccepted(WebSourceDocument source) => Accepted++;
}
