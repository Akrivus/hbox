using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

static class Tests
{
    static async Task Main()
    {
        Check(SoccerModeMetadata.Read("Mode: Soccer", "Mode") == "Soccer", "plain mode");
        Check(SoccerModeMetadata.Read("**Mode:** **Soccer**", "Mode") == "Soccer", "formatted mode");
        Check(SoccerModeMetadata.Read("# Mode: `Soccer`\nHome: **Germany**", "Home") == "Germany", "formatted actor metadata");
        Check(SoccerModeMetadata.Read("They discuss Mode: Soccer\nModeled: Soccer", "Mode") == "", "prose does not trigger mode");
        Check(!SoccerModeMetadata.IsMatchRequest("Mode: Soccer", "Previous story:\nMode: Soccer\nHome: America\nAway: Germany", "soccer-postgame"),
            "postgame cannot launch a match from inherited soccer metadata");
        Check(!SoccerModeMetadata.IsMatchRequest("Mode: Soccer", "SoccerPhase: Postgame\nPrevious story:\nMode: Soccer", "manual"),
            "explicit postgame phase prevents restart");
        Check(SoccerModeMetadata.IsMatchRequest("Mode: **Soccer**", "Home: America\nAway: Germany", "manual"), "normal soccer request still launches");
        var mixed = SoccerRosterService.Parse("```json\n{\"home\":[\"George Washington\",\"Joe Biden\",\"Donald Trump\"],\"away\":[\"Napoleon Bonaparte\"]}\n```", 3, 1, new[] { "America" });
        Check(mixed[0][0] == "George Washington" && mixed[0][2] == "Donald Trump", "mixed eras");
        Reject("{\"home\":[\"America\"],\"away\":[\"Napoleon\"]}", "cast name");
        Reject("{\"home\":[\"Joe Biden\"],\"away\":[\"joe biden\"]}", "duplicate across sides");
        Reject("{\"home\":[],\"away\":[\"Napoleon\"]}", "short roster");
        Reject("{\"home\":[42],\"away\":[\"Napoleon\"]}", "non-name");
        Reject("{\"home\":[\"Joe: hello\"],\"away\":[\"Napoleon\"]}", "dialogue injection");
        var fallback = SoccerRosterService.Fallback("America", new[] { "Joe", "Joe", "", "Donald" }, 11);
        Check(fallback.Length == 11 && fallback.Distinct().Count() == 11, "complete fallback without duplicates");
        Check(SoccerRosterService.Fallback("France", null, 11).Length == 11, "missing fallback");
        LLM.Output = "invalid JSON";
        var failed = false;
        try { await new SoccerRosterService().Generate(new ChatGenerator(), "America", "France", 11, 11, "story", Array.Empty<string>()); }
        catch (Newtonsoft.Json.JsonException) { failed = true; }
        Check(failed, "malformed generation fails the phase");
        LLM.Pending = new TaskCompletionSource<string>();
        var waiting = new SoccerRosterService().Generate(new ChatGenerator(), "America", "France", 1, 1, "story", Array.Empty<string>());
        Check(!waiting.IsCompleted, "generation awaits the model");
        LLM.Pending.SetResult("{\"home\":[\"George Washington\"],\"away\":[\"Napoleon\"]}");
        Check((await waiting)[0][0] == "George Washington", "delayed result is retained");
        var directory = Path.Combine("Temp", "SoccerTests", "events-" + Guid.NewGuid().ToString("N"));
        var store = new SoccerEventStore(directory);
        var now = DateTimeOffset.Parse("2026-10-07T18:00:00-07:00");
        var due = new SoccerScheduledMatch { Id = "fixture-1", Home = "America", Away = "France", ScheduledAt = now };
        due.Validate();
        var future = new SoccerScheduledMatch { Id = "fixture-2", Home = "America", Away = "France", ScheduledAt = now.AddHours(1) };
        Check(store.NextDue(new[] { future, due }, now)?.Id == due.Id, "due schedule ignores future event");
        store.SetStatus(due.Id, "queued");
        store = new SoccerEventStore(directory);
        Check(store.NextDue(new[] { due, future }, now.AddHours(2)) == null, "outstanding event survives reload and blocks duplicate dispatch");
        store.SetStatus(due.Id, "started");
        store.SaveReport(new SoccerMatchReport { MatchId = "match-1", ScheduledEventId = due.Id,
            Home = "America", Away = "France", HomeScore = 2, AwayScore = 1, CompletedAt = now });
        store = new SoccerEventStore(directory);
        Check(store.Status(due.Id) == "completed" && store.NextDue(new[] { due, future }, now.AddHours(2))?.Id == future.Id,
            "completed report releases next event without replaying old event");
        var report = Newtonsoft.Json.JsonConvert.DeserializeObject<SoccerMatchReport>(File.ReadAllText(Path.Combine(directory, "reports", "match-1.json")));
        Check(report.HomeScore == 2 && report.CompletedAt == now && report.ScheduledEventId == due.Id, "report survives serialization");
        ReactionChecks(store, directory, now);
        var badId = false;
        try { store.SaveReport(new SoccerMatchReport { MatchId = "../outside" }); }
        catch (ArgumentException) { badId = true; }
        Check(badId, "report ID cannot escape directory");
        var actors = new[] { "America", "France", "Germany", "Japan" };
        var series = new SoccerScheduleConfig { Id = "live-football", Recurrence = new RecurringEventPattern { EveryMinutes = 60 } };
        Check(store.GetRollingFixture(series, new[] { "Israel", "Palestine" }, now) == null, "first interval waits from registration");
        store = new SoccerEventStore(directory);
        var opening = store.GetRollingFixture(series, new[] { "Israel", "Palestine" }, now.AddHours(1), new Random(4));
        store.SetStatus(opening.Id, "started");
        store.SaveReport(new SoccerMatchReport { MatchId = "series-result-1", ScheduledEventId = opening.Id, SeriesId = series.Id,
            Home = opening.Home, Away = opening.Away, HomeScore = 2, AwayScore = 0, CompletedAt = now });
        Check(store.GetRollingFixture(series, actors, now.AddMinutes(30)) == null, "rolling series waits after completion");
        var changed = store.GetRollingFixture(series, new[] { opening.Home, opening.Away, "Germany" }, now.AddHours(1), new Random(5));
        Check(changed.Home == "Germany" || changed.Away == "Germany", "rolling selection admits new actors and avoids immediate rematch");
        Check(changed.Premise.Contains("winner: " + opening.Home), "rolling lore preserves actual winner");
        store = new SoccerEventStore(directory);
        var saved = store.GetRollingFixture(series, new[] { "America", "Japan" }, now.AddHours(1), new Random(99));
        Check(saved.Id == changed.Id && saved.Home == changed.Home && saved.Away == changed.Away, "queued fixture stays stable on reload");
        store.SaveReport(new SoccerMatchReport { MatchId = "series-result-2", ScheduledEventId = saved.Id, SeriesId = series.Id,
            Home = saved.Home, Away = saved.Away, HomeScore = 1, AwayScore = 1, CompletedAt = now.AddHours(1) });
        var latest = store.GetRollingFixture(series, new[] { "America", "Japan" }, now.AddHours(2), new Random(3));
        Check(new[] { latest.Home, latest.Away }.OrderBy(n => n).SequenceEqual(new[] { "America", "Japan" }) && latest.Premise.Contains("draw"),
            "rolling series follows refreshed cast and remembers draws");
        Console.WriteLine("11 soccer roster checks passed.");
        Console.WriteLine("5 soccer scheduling and report checks passed.");
        var weekly = new RecurringEventPattern { Days = new[] { DayOfWeek.Wednesday, DayOfWeek.Friday }, Times = new[] { "19:00", "07:00" } };
        Check(weekly.NextAfter(now) == now.AddHours(1), "calendar sorts local times");
        Check(weekly.NextAfter(now.AddHours(1)) == DateTimeOffset.Parse("2026-10-09T07:00:00-07:00"), "calendar advances weekday at exact boundary");
        Check(weekly.NextAfter(now.AddDays(8)) > now.AddDays(8), "calendar skips missed slots");
        var invalid = false;
        try { new RecurringEventPattern { Times = new[] { "25:00" }, Days = new[] { DayOfWeek.Monday } }.Validate(); }
        catch (ArgumentException) { invalid = true; }
        Check(invalid, "invalid calendar pattern rejected");
        var jsonPattern = Newtonsoft.Json.JsonConvert.DeserializeObject<RecurringEventPattern>("{\"Days\":[\"Wednesday\",\"Friday\"],\"Times\":[\"19:00\"],\"TimeZoneId\":\"US Mountain Standard Time\"}");
        Check(jsonPattern.NextAfter(now) == now.AddHours(1), "documented JSON weekday names deserialize");
        var calendarConfig = new SoccerScheduleConfig { Id = "weekly", Recurrence = weekly };
        Check(store.GetRollingFixture(calendarConfig, actors, now) == null, "calendar waits for first slot");
        var catchup = store.GetRollingFixture(calendarConfig, actors, now.AddDays(10));
        store.SetStatus(catchup.Id, "started");
        Check(store.GetRollingFixture(calendarConfig, actors, now.AddDays(10))?.Id == catchup.Id, "missed slots preserve one pending fixture");
        store.SaveReport(new SoccerMatchReport { MatchId = "weekly-result", ScheduledEventId = catchup.Id, SeriesId = calendarConfig.Id,
            Home = catchup.Home, Away = catchup.Away, CompletedAt = now.AddDays(10) });
        Check(store.GetRollingFixture(calendarConfig, actors, now.AddDays(10)) == null, "completed catchup advances beyond missed slots");
        var multiDirectory = Path.Combine(directory, "multiple");
        var multi = new SoccerEventStore(multiDirectory);
        var slow = new SoccerScheduleConfig { Id = "evening", Recurrence = new RecurringEventPattern { EveryMinutes = 120 } };
        var fast = new SoccerScheduleConfig { Id = "hourly", Recurrence = new RecurringEventPattern { EveryMinutes = 60 } };
        multi.GetRollingFixture(slow, actors, now);
        multi.GetRollingFixture(fast, actors, now);
        multi = new SoccerEventStore(multiDirectory);
        var slowMatch = multi.GetRollingFixture(slow, actors, now.AddHours(3));
        var fastMatch = multi.GetRollingFixture(fast, actors, now.AddHours(3));
        Check(multi.NextDue(new[] { slowMatch, fastMatch }, now.AddHours(3)).Id == fastMatch.Id, "multiple schedules dispatch earliest due regardless of array order");
        multi.SetStatus(fastMatch.Id, "queued");
        Check(multi.NextDue(new[] { slowMatch }, now.AddHours(3)) == null, "schedules share one outstanding match slot");
        multi.SaveReport(new SoccerMatchReport { MatchId = "hourly-result", ScheduledEventId = fastMatch.Id, SeriesId = fast.Id,
            Home = fastMatch.Home, Away = fastMatch.Away, HomeScore = 3, AwayScore = 0, CompletedAt = now.AddHours(3) });
        Check(multi.NextDue(new[] { slowMatch }, now.AddHours(3)).Id == slowMatch.Id, "completion releases other schedule");
        var nextFast = multi.GetRollingFixture(fast, actors, now.AddHours(4));
        Check(nextFast.Premise.Contains("3-0") && !slowMatch.Premise.Contains("3-0"), "schedule histories stay independent");
        Console.WriteLine("4 multiple schedule checks passed.");
        Console.WriteLine("8 recurrence checks passed.");
        Console.WriteLine("5 rolling schedule checks passed.");
        Console.WriteLine("4 soccer metadata checks passed.");
        Console.WriteLine("3 postgame match eligibility checks passed.");
    }
    static void ReactionChecks(SoccerEventStore store, string directory, DateTimeOffset now)
    {
        var capture = new SoccerReactionCapture();
        capture.BeginMatch("reaction-match", "polbots");
        var played = new DialoguePlayback("polbots", "fixture-chat", 7, "reaction-match", "packet-1",
            "Scotland", "That's my goal! — allegedly.", "[cheers] That's my goal! — allegedly.", now, 100);
        // Merely generated/activated, or started but interrupted: no completion, no quotation.
        Check(capture.Snapshot().Length == 0, "generated but unplayed dialogue absent");
        capture.Started(played);
        Check(capture.Snapshot().Length == 0, "audio start alone is not an aired quote");
        var completed = played.Complete(now.AddSeconds(2), 102);
        capture.Completed(completed);
        capture.Completed(completed);
        var reaction = capture.Snapshot().Single();
        Check(reaction.Speaker == "Scotland" && reaction.SpokenText == played.SpokenText &&
            reaction.OriginalDialogue == played.OriginalDialogue, "exact spoken and original text retained separately");
        Check(reaction.MatchId == "reaction-match" && reaction.SourceChatId == "fixture-chat" &&
            reaction.SourceInterruptId == "packet-1" && reaction.SourceNodeIndex == 7 &&
            reaction.PlaybackId == played.PlaybackId, "match and source provenance retained once");
        Check(reaction.StartedAt == now && reaction.CompletedAt == now.AddSeconds(2) &&
            reaction.StartedDspTime == 100 && reaction.CompletedDspTime == 102, "wall and DSP timing preserved");
        var unknown = new DialoguePlayback("polbots", "unknown", 0, null, null, "Netherlands", "No!", null, now, 100);
        capture.Completed(unknown.Complete(now.AddSeconds(1), 101));
        Check(capture.Snapshot().Length == 1, "completion without active-match start rejected");
        var unrelated = new DialoguePlayback("RomeBots", "other", 0, null, null, "Livia", "No!", null, now, 100);
        capture.Started(unrelated);
        capture.Completed(unrelated.Complete(now.AddSeconds(1), 101));
        var stale = new DialoguePlayback("polbots", "old", 0, "old-match", "old-packet", "Scotland", "No!", null, now, 100);
        capture.Started(stale);
        capture.Completed(stale.Complete(now.AddSeconds(1), 101));
        Check(capture.Snapshot().Length == 1, "other channels and stale interrupt matches rejected");
        capture.Started(unknown); // Normal host dialogue inherits the active match at audio start.
        capture.Completed(unknown.Complete(now.AddSeconds(1), 101));
        Check(capture.Snapshot().Length == 2 && capture.Snapshot()[1].SourceInterruptId == null,
            "ordinary match-time host dialogue carries chat provenance without invented packet ID");
        var interrupted = new DialoguePlayback("polbots", "interrupted", 1, null, null, "France", "Wait!", null, now, 100);
        capture.Started(interrupted);
        capture.EndMatch();
        capture.Completed(interrupted.Complete(now.AddSeconds(2), 102));
        Check(capture.Snapshot().Length == 0, "late completion after final whistle rejected");
        capture.BeginMatch("next-match", "polbots");
        capture.Completed(interrupted.Complete(now.AddSeconds(2), 102));
        Check(capture.Snapshot().Length == 0, "pending speech cannot migrate into next match");
        store.SaveReport(new SoccerMatchReport { MatchId = "reaction-match", Home = "Scotland", Away = "Netherlands",
            HomeScore = 1, AwayScore = 0, CompletedAt = now, KeyEvents = new[] { "Authoritative goal" },
            Reactions = new[] { reaction } });
        var path = Path.Combine(directory, "reports", "reaction-match.json");
        var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<SoccerMatchReport>(File.ReadAllText(path));
        Check(reloaded.Reactions.Single().SpokenText == played.SpokenText &&
            reloaded.Reactions[0].OriginalDialogue == played.OriginalDialogue &&
            reloaded.Reactions[0].SourceInterruptId == "packet-1" && reloaded.Reactions[0].CompletedDspTime == 102 &&
            reloaded.Reactions[0].StartedAt == now, "reaction payload survives report JSON roundtrip");
        Check(reloaded.HomeScore == 1 && reloaded.AwayScore == 0 && reloaded.KeyEvents.Single() == "Authoritative goal",
            "subjective reaction cannot replace score or event data");
        var legacyPath = Path.Combine(directory, "reports", "historical.json");
        File.WriteAllText(legacyPath, "{\"MatchId\":\"historical\",\"HomeScore\":3,\"KeyEvents\":[\"Old result\"]}");
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<SoccerMatchReport>(File.ReadAllText(legacyPath));
        Check(legacy.Reactions == null && legacy.HomeScore == 3 && legacy.KeyEvents[0] == "Old result",
            "historical JSON without reactions remains compatible");
        Check(!Newtonsoft.Json.JsonConvert.SerializeObject(legacy).Contains("Reactions"), "absent legacy metadata stays optional");
        store = new SoccerEventStore(directory); // Exercises actual store reload with mixed old/new reports.
        store.SaveReport(reloaded);
        store.SaveReport(legacy);
        Check(Newtonsoft.Json.JsonConvert.DeserializeObject<SoccerMatchReport>(File.ReadAllText(path)).Reactions.Length == 1 &&
            Newtonsoft.Json.JsonConvert.DeserializeObject<SoccerMatchReport>(File.ReadAllText(legacyPath)).Reactions == null,
            "store reload and resave preserve enriched and historical reports");
        Console.WriteLine("17 soccer reaction provenance checks passed.");
    }

    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    static void Reject(string json, string name)
    {
        try { SoccerRosterService.Parse(json, 1, 1, new[] { "America" }); }
        catch (FormatException) { return; }
        throw new Exception("Accepted " + name);
    }
}

// Runtime boundaries are stubbed; parsing and fallback use the production implementation.
public class ChatGenerator { public object ManagerContext => null; }
public class PromptResolver
{
    public PromptResolver(object context, params string[] parts) { }
    public Task Resolve(params string[] values) => Task.CompletedTask;
}
public static class LLM
{
    public static string Output;
    public static TaskCompletionSource<string> Pending;
    public static Task<string> CompleteAsync(PromptResolver prompt, object chat) => Pending?.Task ?? Task.FromResult(Output);
}
namespace UnityEngine { public static class Debug { public static void LogWarning(string text) { } } }
