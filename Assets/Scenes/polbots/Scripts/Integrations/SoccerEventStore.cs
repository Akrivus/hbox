using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Data only: polbots owns dispatch; other channels consume completed reports.
public sealed class SoccerScheduledMatch
{
    public string SeriesId { get; set; }
    public string TournamentId { get; set; }
    public string Id { get; set; }
    public string Home { get; set; }
    public string Away { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public string Premise { get; set; }

    public void Validate()
    {
        SoccerEventStore.ValidateId(Id);
        if (string.IsNullOrWhiteSpace(Home) || string.IsNullOrWhiteSpace(Away) ||
            Home.IndexOfAny(new[] { '\r', '\n' }) >= 0 || Away.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
            string.Equals(Home, Away, StringComparison.OrdinalIgnoreCase) || ScheduledAt == default)
            throw new ArgumentException("Scheduled soccer needs distinct single-line actor names and a timestamp with offset.");
    }

    public string BuildPrompt() =>
        $"Mode: Soccer\nHome: {Home}\nAway: {Away}\nSoccerEventId: {Id}\n" +
        $"ScheduledAt: {ScheduledAt:O}\nA scheduled match between {Home} and {Away}.\n{Premise}";
}

public sealed class SoccerMatchReport
{
    public string SavedMatchId { get; set; }
    public int? Seed { get; set; }
    public string SeriesId { get; set; }
    public string TournamentId { get; set; }
    public string MatchId { get; set; }
    public string ScheduledEventId { get; set; }
    public string ChannelKey { get; set; }
    public string Home { get; set; }
    public string Away { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string RosterContext { get; set; }
    public string[] KeyEvents { get; set; }
}

// Shared recurrence vocabulary: interval, or weekly wall-clock slots in a named timezone.
public sealed class RecurringEventPattern
{
    public int EveryMinutes { get; set; } = 60;
    public DayOfWeek[] Days { get; set; } = Array.Empty<DayOfWeek>();
    public string[] Times { get; set; } = Array.Empty<string>();
    public string TimeZoneId { get; set; } = "US Mountain Standard Time";

    public void Validate()
    {
        if (Times == null || Days == null) throw new ArgumentException("Recurrence days and times cannot be null.");
        if (Times.Length == 0)
        {
            if (EveryMinutes < 1) throw new ArgumentException("Recurrence interval must be positive.");
            return;
        }
        if (Days.Length == 0 || Days.Any(day => (int)day < 0 || (int)day > 6))
            throw new ArgumentException("Calendar recurrence needs weekdays.");
        foreach (var time in Times) ParseTime(time);
        TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    }

    private static TimeSpan ParseTime(string time)
    {
        if (!TimeSpan.TryParseExact(time, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException("Recurrence times must be HH:mm in the configured timezone.");
        return value;
    }

    public DateTimeOffset NextAfter(DateTimeOffset after)
    {
        Validate();
        if (Times.Length == 0) return after.AddMinutes(EveryMinutes);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var date = TimeZoneInfo.ConvertTime(after, zone).Date;
        for (var day = 0; day <= 7; day++)
        {
            var nextDate = date.AddDays(day);
            if (!Days.Contains(nextDate.DayOfWeek)) continue;
            foreach (var time in Times.Select(ParseTime).Distinct().OrderBy(value => value))
            {
                var local = DateTime.SpecifyKind(nextDate.Add(time), DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(local)) continue; // Spring-forward slots do not exist.
                // Ambiguous fall-back times use the standard-time occurrence once.
                var next = new DateTimeOffset(local, zone.GetUtcOffset(local));
                if (next > after) return next;
            }
        }
        throw new ArgumentException("No valid recurrence slot in the next week.");
    }
}

public sealed class SoccerScheduleConfig
{
    public string Id { get; set; }
    public string Name { get; set; } = "Polbots Football";
    public RecurringEventPattern Recurrence { get; set; } = new RecurringEventPattern();
    public bool WinnerStays { get; set; }
    public string Lore { get; set; } = "The world's arguments keep finding their way onto the football pitch.";
}

public sealed class SoccerRollingScheduleState
{
    public int Sequence { get; set; }
    public DateTimeOffset NextAt { get; set; }
    public SoccerScheduledMatch Pending { get; set; }
}

public sealed class SoccerEventStore
{
    private readonly string directory;
    private readonly Dictionary<string, string> status;
    private readonly List<SoccerMatchReport> reports = new List<SoccerMatchReport>();
    private readonly Dictionary<string, SoccerRollingScheduleState> rollingSchedules = new Dictionary<string, SoccerRollingScheduleState>();
    public SoccerEventStore(string directory)
    {
        this.directory = directory;
        var path = Path.Combine(directory, "schedule.json");
        status = File.Exists(path)
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();
        var reportPath = Path.Combine(directory, "reports");
        if (Directory.Exists(reportPath))
            foreach (var file in Directory.GetFiles(reportPath, "*.json"))
                reports.Add(JsonConvert.DeserializeObject<SoccerMatchReport>(File.ReadAllText(file))
                    ?? throw new FormatException("Empty soccer report: " + file));
    }

    public SoccerScheduledMatch GetRollingFixture(SoccerScheduleConfig config, IEnumerable<string> eligible,
        DateTimeOffset now, Random random = null)
    {
        ValidateId(config.Id);
        config.Recurrence.Validate();
        var path = Path.Combine(directory, "series", config.Id + ".json");
        if (!rollingSchedules.TryGetValue(config.Id, out var state))
        {
            state = File.Exists(path) ? JsonConvert.DeserializeObject<SoccerRollingScheduleState>(File.ReadAllText(path))
                : new SoccerRollingScheduleState { NextAt = config.Recurrence.NextAfter(now) };
            if (state == null) throw new FormatException("Empty rolling soccer schedule.");
            rollingSchedules[config.Id] = state;
            if (!File.Exists(path)) Write(path, state);
        }
        if (state.Pending != null)
        {
            var outcome = Status(state.Pending.Id);
            if (outcome != "completed" && outcome != "cancelled") return state.Pending;
            var completed = reports.FirstOrDefault(report => report.ScheduledEventId == state.Pending.Id);
            state.NextAt = config.Recurrence.NextAfter(completed?.CompletedAt ?? now);
            state.Pending = null;
            Write(path, state);
        }
        if (now < state.NextAt || status.Values.Any(value => value == "queued" || value == "started")) return null;
        // Refresh the cast for every new fixture; no frozen tournament entrant list.
        var candidates = eligible.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        if (candidates.Count < 2) return null;
        var history = reports.Where(report => report.SeriesId == config.Id).OrderByDescending(report => report.CompletedAt).ToList();
        var previous = history.FirstOrDefault();
        var winner = previous == null || previous.HomeScore == previous.AwayScore ? null
            : previous.HomeScore > previous.AwayScore ? previous.Home : previous.Away;
        var pairs = new List<string[]>();
        for (var i = 0; i < candidates.Count; i++)
            for (var j = i + 1; j < candidates.Count; j++) pairs.Add(new[] { candidates[i], candidates[j] });
        if (config.WinnerStays && winner != null && candidates.Contains(winner))
            pairs = pairs.Where(pair => pair.Contains(winner)).ToList();
        var fresh = pairs.Where(pair => previous == null ||
            !(pair.Contains(previous.Home) && pair.Contains(previous.Away))).ToList();
        if (fresh.Count > 0) pairs = fresh;
        random = random ?? new Random();
        var selected = pairs[random.Next(pairs.Count)];
        if (random.Next(2) == 1) selected = selected.Reverse().ToArray();
        var context = $"Series: {config.Name}\n{config.Lore}\n" +
            "Recent recorded matches and winners:\n" + (history.Count == 0 ? "No completed series matches yet." :
                string.Join("\n", history.Take(8).Select(report =>
                    $"{report.Home} {report.HomeScore}-{report.AwayScore} {report.Away}; " +
                    (report.HomeScore == report.AwayScore ? "draw" : "winner: " + (report.HomeScore > report.AwayScore ? report.Home : report.Away))))) +
            "\nThe participants change between fixtures. Remember actual winners and defeats, and connect today's argument to that history. Do not invent past results.";
        state.Pending = new SoccerScheduledMatch { Id = config.Id + "-match-" + (++state.Sequence), SeriesId = config.Id,
            Home = selected[0], Away = selected[1], ScheduledAt = state.NextAt, Premise = context };
        state.Pending.Validate();
        Write(path, state);
        return state.Pending;
    }

    public SoccerScheduledMatch NextDue(IEnumerable<SoccerScheduledMatch> matches, DateTimeOffset now)
    {
        // One outstanding generation/match at a time, including across scene reloads.
        if (status.Values.Any(value => value == "queued" || value == "started")) return null;
        return (matches ?? Enumerable.Empty<SoccerScheduledMatch>())
            .Where(match => match != null && match.ScheduledAt <= now && !status.ContainsKey(match.Id ?? ""))
            .OrderBy(match => match.ScheduledAt).FirstOrDefault();
    }

    public string Status(string id) => status.TryGetValue(id, out var value) ? value : null;

    public void SetStatus(string id, string value)
    {
        ValidateId(id);
        status[id] = value;
        Write(Path.Combine(directory, "schedule.json"), status);
    }

    public void SaveReport(SoccerMatchReport report)
    {
        ValidateId(report.MatchId);
        Write(Path.Combine(directory, "reports", report.MatchId + ".json"), report);
        reports.RemoveAll(previous => previous.MatchId == report.MatchId);
        reports.Add(report);
        if (!string.IsNullOrWhiteSpace(report.ScheduledEventId)) SetStatus(report.ScheduledEventId, "completed");
    }

    public static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 ||
            id.Any(c => !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                !(c >= '0' && c <= '9') && c != '-' && c != '_'))
            throw new ArgumentException("Soccer event IDs must use letters, numbers, hyphens or underscores (maximum 80 characters).");
    }

    private static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
