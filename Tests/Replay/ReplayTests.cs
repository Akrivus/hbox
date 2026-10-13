using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

internal static class ReplayTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "hbox-replay-" + Guid.NewGuid().ToString("N"));
    private static int passed;
    private static object Call(FolderSource source, string method, params object[] args) =>
        typeof(FolderSource).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, args);
    private static void Set(FolderSource source, string field, object value) =>
        typeof(FolderSource).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, value);
    private static T Get<T>(FolderSource source, string field) =>
        (T)typeof(FolderSource).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Test(string name, Func<Task> action)
    {
        await action(); passed++; Console.WriteLine("PASS " + name);
    }
    private static FolderSource Library(bool fallback = true, bool empty = false)
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!empty)
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures")))
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
        var source = new FolderSource
        {
            ReplayDirectory = directory, MaxReplayAgeInMinutes = 1440,
            ReplaysPerBatch = 1, ReplayRate = 80, ReplayOlderAfterRecentExhausted = fallback
        };
        Set(source, "replayClock", (Func<DateTimeOffset>)(() => Now));
        Set(source, "manifest", Call(source, "LoadManifest"));
        Call(source, "BootstrapManifest");
        return source;
    }
    private static List<ReplayManifestEntry> Candidates(FolderSource source)
    {
        var entries = (IEnumerable<ReplayManifestEntry>)Call(source, "GetCandidateEntries", source.ReplayDirectory);
        return ((IReadOnlyList<ReplayManifestEntry>)Call(source, "GetNextReplayCandidates", entries)).ToList();
    }
    private static void History(FolderSource source, params string[] slugs) => Set(source, "replays", slugs.ToList());
    private static List<ReplayManifestEntry> Entries(FolderSource source) => Get<ReplayManifest>(source, "manifest").entries;
    private static void Bind(FolderSource source)
    {
        var context = new ChatManagerContext();
        Set(source, "boundContext", context);
        ChatManager.Instance.CurrentContext = context;
    }
    public static async Task<int> Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            await Run();
            Console.WriteLine(passed + " replay regression tests passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(Root, true); }
    }
    private static async Task Run()
    {
        await Test("saved today fixtures precede yesterday despite yesterday votes", () =>
        {
            var source = Library();
            Check(Candidates(source).Select(e => e.slug).SequenceEqual(new[] { "today-high", "today-low", "yesterday-high", "yesterday-low" }), "Age tiers or vote ranking changed");
            return Task.CompletedTask;
        });
        await Test("partial recent rotation and exhaustion open older saved bulletins", () =>
        {
            var source = Library();
            History(source, "TODAY-HIGH");
            Check(Candidates(source).First().slug == "today-low", "History must be case insensitive");
            History(source, "today-high", "today-low");
            Check(Candidates(source).First().slug == "yesterday-high", "Older episode excluded after recent exhaustion");
            History(source, "today-high", "today-low", "yesterday-high");
            Check(Candidates(source).Single().slug == "yesterday-low", "Older rotation skipped");
            return Task.CompletedTask;
        });
        await Test("full exhaustion starts another complete rotation", () =>
        {
            var source = Library();
            History(source, Entries(source).Select(e => e.slug).ToArray());
            Check(Candidates(source).First().slug == "today-high", "Today cannot be rewatched");
            // Exercise the production selection/queue history path.
            Set(source, "fileName", Path.Combine(source.ReplayDirectory, "history.txt"));
            Call(source, "AddReplayToList", "today-high");
            Check(Get<List<string>>(source, "replays").SequenceEqual(new[] { "today-high" }), "Rotation history was not reset");
            Check(Candidates(source).First().slug == "today-low", "Second rotation stuck on first episode");
            return Task.CompletedTask;
        });
        await Test("eligible older episode beats cooling recent episode", () =>
        {
            var source = Library();
            foreach (var e in Entries(source).Where(e => e.slug.StartsWith("today")))
                e.replayEligibleAt = Now.AddMinutes(10).ToString("O");
            Check(Candidates(source).First().slug == "yesterday-high", "Cooldown ignored across age tiers");
            History(source, "yesterday-high", "yesterday-low");
            Check(Candidates(source).First().slug == "yesterday-high", "Cooling unseen episode beat eligible replay");
            return Task.CompletedTask;
        });
        await Test("cooldown expires at exact boundary and all-cooling library still plays", () =>
        {
            var source = Library();
            foreach (var e in Entries(source)) e.replayEligibleAt = Now.AddMinutes(10).ToString("O");
            Check(Candidates(source).First().slug == "today-high", "All-cooling library went silent");
            Entries(source).Single(e => e.slug == "yesterday-low").replayEligibleAt = Now.ToString("O");
            Check(Candidates(source).Single().slug == "yesterday-low", "Exact eligibility boundary excluded");
            return Task.CompletedTask;
        });
        await Test("production playback event updates cooldown and replay counts", () =>
        {
            var source = Library(); Bind(source);
            Call(source, "SubscribeToRuntimeEvents");
            Get<ChatManagerContext>(source, "boundContext").RaiseLoaded(new Chat { Key = "pbnn", FileName = "today-high", Title = "Today", NewEpisode = false });
            var entry = Entries(source).Single(e => e.slug == "today-high");
            Check(entry.timesReplayed == 1 && DateTimeOffset.Parse(entry.lastPlayedAt) == Now, "Playback bookkeeping");
            Check(DateTimeOffset.Parse(entry.replayEligibleAt) == Now.AddMinutes(60), "24-hour policy cooldown");
            Check(Candidates(source).First().slug == "today-low", "Cooling bulletin selected first");
            var reloaded = (ReplayManifest)Call(source, "LoadManifest");
            Check(reloaded.entries.Single(e => e.slug == "today-high").timesReplayed == 1, "Manifest update not saved");
            return Task.CompletedTask;
        });
        await Test("persisted remembered history resumes older rotation after restart", () =>
        {
            var source = Library(); Bind(source);
            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = source.ReplayDirectory;
                File.WriteAllLines("replays-pbnn.txt", new[] { "today-high", "today-low" });
                Set(source, "replays", Call(source, "LoadReplays"));
                Check(Candidates(source).First().slug == "yesterday-high", "Saved history did not resume exhaustion fallback");
            }
            finally { Environment.CurrentDirectory = previous; }
            return Task.CompletedTask;
        });
        await Test("new recent bulletin gets priority during older rotation", () =>
        {
            var source = Library(); Bind(source);
            History(source, "today-high", "today-low", "yesterday-high");
            File.WriteAllText(Path.Combine(source.ReplayDirectory, "new-today.json"), "{\"Key\":\"pbnn\",\"FileName\":\"new-today\"}");
            Call(source, "SubscribeToRuntimeEvents");
            Get<ChatManagerContext>(source, "boundContext").RaiseQueued(new Chat { Key = "pbnn", FileName = "new-today", Title = "New", NewEpisode = true });
            Check(Candidates(source).First().slug == "new-today", "New recent bulletin failed to lead");
            return Task.CompletedTask;
        });
        await Test("legacy channels retain recent-only filtering after recent exhaustion", () =>
        {
            var source = Library(false);
            History(source, "today-high", "today-low");
            Check(Candidates(source).Count == 2 && Candidates(source).All(e => e.slug.StartsWith("today")), "Legacy behavior changed");
            foreach (var e in Entries(source)) e.generatedAt = Now.AddDays(-2).ToString("O");
            Check(Candidates(source).First().slug == "yesterday-high", "Legacy no-recent fallback changed");
            Check(!new FolderConfigs().ReplayOlderAfterRecentExhausted, "Fallback became global default");
            return Task.CompletedTask;
        });
        await Test("missing and blank-slug entries cannot block exhaustion", () =>
        {
            var source = Library();
            Entries(source).Add(new ReplayManifestEntry { slug = "missing", generatedAt = Now.ToString("O") });
            Entries(source).Add(new ReplayManifestEntry { slug = "" });
            History(source, "today-high", "today-low", "yesterday-high");
            Check(Candidates(source).Single().slug == "yesterday-low", "Invalid entries contaminated candidates");
            return Task.CompletedTask;
        });
        await Test("24-hour boundary belongs to preferred tier", () =>
        {
            var source = Library();
            var e = Entries(source).Single(e => e.slug == "yesterday-high");
            e.generatedAt = Now.AddMinutes(-1440).ToString("O");
            Check(Candidates(source).First().slug == "yesterday-high", "Exact age boundary excluded");
            e.generatedAt = Now.AddMinutes(-1440).AddTicks(-1).ToString("O");
            Check(Candidates(source).First().slug == "today-high", "Older-than-boundary preferred");
            return Task.CompletedTask;
        });
        await Test("empty library waits without loading or generating", async () =>
        {
            var source = Library(empty: true); Bind(source);
            ChatManager.Instance.Queued.Clear();
            Check(Candidates(source).Count == 0, "Empty selection");
            await (Task)Call(source, "FetchFiles", 1);
            Check(ChatManager.Instance.Queued.Count == 0, "Empty library queued a chat");
        });
        await Test("saved JSON fetch queues today then yesterday after recent exhaustion", async () =>
        {
            var source = Library(); Bind(source);
            Set(source, "fileName", Path.Combine(source.ReplayDirectory, "history.txt"));
            ChatManager.Instance.Queued.Clear();
            await (Task)Call(source, "FetchFiles", 1);
            Check(ChatManager.Instance.Queued.Single().FileName == "today-high", "Saved fixture fetch");
            History(source, "today-high", "today-low");
            ChatManager.Instance.Queued.Clear();
            await (Task)Call(source, "FetchFiles", 1);
            var chat = ChatManager.Instance.Queued.Single();
            Check(chat.Key == "pbnn" && chat.FileName == "yesterday-high", "Older fixture ownership/load");
        });
        await Test("vote score still controls order within yesterday tier", () =>
        {
            var source = Library(); Bind(source);
            History(source, "today-high", "today-low");
            Call(source, "ApplyVoteInternal", "yesterday-low", 200, 0, "test", null);
            Check(Candidates(source).First().slug == "yesterday-low", "Audience vote ranking lost");
            return Task.CompletedTask;
        });
    }
}
