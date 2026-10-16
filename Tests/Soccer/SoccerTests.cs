using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

static class Tests
{
    static async Task Main()
    {
        SavedMatchTests.Run();
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
