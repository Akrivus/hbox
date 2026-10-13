using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
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
        Set(source, "fileName", Path.Combine(directory, "history.txt"));
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
        await Test("rapid production history updates publish complete bounded legacy snapshots", () =>
        {
            var source = Library(false); source.ReplayRate = 3;
            for (var i = 0; i < 50; i++)
            {
                Call(source, "AddReplayToList", "selection-" + i);
                var expected = Enumerable.Range(Math.Max(0, i - 2), Math.Min(i + 1, 3)).Select(n => "selection-" + n);
                Check(File.ReadAllLines(Get<string>(source, "fileName")).SequenceEqual(expected), "Write incomplete/out of order or history limit changed");
            }
            Check(!Directory.GetFiles(source.ReplayDirectory, "*.tmp").Any(), "Staging leaked");
            return Task.CompletedTask;
        });
        await Test("overlapping updates serialize staging and publication", async () =>
        {
            var source = Library(false);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var secondStarted = new ManualResetEventSlim();
            var snapshots = new List<string[]>();
            Set(source, "publishReplayHistory", (Action<string, string>)((temporary, destination) =>
            {
                snapshots.Add(File.ReadAllLines(temporary));
                if (snapshots.Count == 1)
                {
                    entered.Set();
                    Check(release.Wait(TimeSpan.FromSeconds(10)), "Publication release timed out");
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }));
            var first = Task.Run(() => Call(source, "AddReplayToList", "first"));
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(10)), "First update never entered publication");
                var second = Task.Run(() => { secondStarted.Set(); Call(source, "AddReplayToList", "second"); });
                Check(secondStarted.Wait(TimeSpan.FromSeconds(10)), "Second update never started");
                release.Set();
                await Task.WhenAll(first, second);
                Check(snapshots.Count == 2 && snapshots[0].SequenceEqual(new[] { "first" }) && snapshots[1].SequenceEqual(new[] { "first", "second" }), "Snapshots raced");
                Check(File.ReadAllLines(Get<string>(source, "fileName")).SequenceEqual(new[] { "first", "second" }), "Older write overwrote newer history");
            }
            finally { release.Set(); await first; }
        });
        await Test("interrupted publication preserves committed history and restart ignores staging", () =>
        {
            var source = Library(); Bind(source);
            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = source.ReplayDirectory;
                Call(source, "LoadReplays");
                Call(source, "AddReplayToList", "today-high");
                var destination = Path.GetFullPath(Get<string>(source, "fileName"));
                var committed = File.ReadAllBytes(destination);
                UnityEngine.Debug.Warnings.Clear();
                Set(source, "publishReplayHistory", (Action<string, string>)((temporary, final) =>
                {
                    Check(File.ReadAllLines(temporary).SequenceEqual(new[] { "today-high", "today-low" }), "Staging incomplete");
                    Check(File.ReadAllBytes(final).SequenceEqual(committed), "Final changed before publication");
                    throw new IOException("injected publication interruption");
                }));
                Call(source, "AddReplayToList", "today-low");
                Check(File.ReadAllBytes(destination).SequenceEqual(committed), "Failed publish damaged last commit");
                Check(UnityEngine.Debug.Warnings.Any(w => w.Contains("injected publication interruption")), "Failure not reported");
                Check(!Directory.GetFiles(source.ReplayDirectory, "*.tmp").Any(), "Failed staging not cleaned");
                File.WriteAllText(destination + ".abandoned.tmp", "partial");
                var restarted = Library(); Bind(restarted);
                Set(restarted, "replays", Call(restarted, "LoadReplays"));
                Check(Get<List<string>>(restarted, "replays").SequenceEqual(new[] { "today-high" }), "Restart loaded uncommitted staging");
                Check(Candidates(restarted).First().slug == "today-low", "Restart skipped uncommitted selection");
                Set(source, "publishReplayHistory", (Action<string, string>)((temporary, final) => File.Replace(temporary, final, null)));
                Call(source, "AddReplayToList", "yesterday-high");
                Check(File.ReadAllLines(destination).SequenceEqual(new[] { "today-high", "today-low", "yesterday-high" }), "Next update failed to recover complete in-memory snapshot");
            }
            finally { Environment.CurrentDirectory = previous; }
            return Task.CompletedTask;
        });
        await Test("staging write failure is observed and later update recovers", () =>
        {
            var source = Library(false);
            var destination = Path.Combine(source.ReplayDirectory, "missing", "history.txt");
            Set(source, "fileName", destination);
            UnityEngine.Debug.Warnings.Clear();
            Call(source, "AddReplayToList", "first");
            Check(!File.Exists(destination) && UnityEngine.Debug.Warnings.Any(w => w.Contains("history write failed")), "Staging failure not handled");
            Check(Get<List<string>>(source, "replays").SequenceEqual(new[] { "first" }), "Failure broke in-memory rotation");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            Call(source, "AddReplayToList", "second");
            Check(File.ReadAllLines(destination).SequenceEqual(new[] { "first", "second" }), "Failed snapshot not recovered");
            return Task.CompletedTask;
        });
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
            foreach (var slug in Entries(source).Select(e => e.slug))
                Call(source, "AddReplayToList", slug);
            Check(File.ReadAllLines(Get<string>(source, "fileName")).Length == 4, "Initial rotation not saved");
            Check(Candidates(source).First().slug == "today-high", "Today cannot be rewatched");
            // Exercise the production selection/queue history path.
            Set(source, "fileName", Path.Combine(source.ReplayDirectory, "history.txt"));
            Call(source, "AddReplayToList", "today-high");
            Check(Get<List<string>>(source, "replays").SequenceEqual(new[] { "today-high" }), "Rotation history was not reset");
            Check(Candidates(source).First().slug == "today-low", "Second rotation stuck on first episode");
            Check(File.ReadAllLines(Get<string>(source, "fileName")).SequenceEqual(new[] { "today-high" }), "Reset not committed before return");
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
                Call(source, "LoadReplays");
                Call(source, "AddReplayToList", "today-high");
                Call(source, "AddReplayToList", "today-low");
                var restarted = Library(); Bind(restarted);
                Set(restarted, "replays", Call(restarted, "LoadReplays"));
                source = restarted;
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
            Check(File.ReadAllLines(Get<string>(source, "fileName")).SequenceEqual(new[] { "today-high" }), "Queue returned before history commit");
            History(source, "today-high", "today-low");
            ChatManager.Instance.Queued.Clear();
            await (Task)Call(source, "FetchFiles", 1);
            var chat = ChatManager.Instance.Queued.Single();
            Check(chat.Key == "pbnn" && chat.FileName == "yesterday-high", "Older fixture ownership/load");
            Check(File.ReadAllLines(Get<string>(source, "fileName")).SequenceEqual(new[] { "today-high", "today-low", "yesterday-high" }), "Queue history not committed");
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

