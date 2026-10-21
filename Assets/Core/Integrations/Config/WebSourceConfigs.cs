using System;

// Batch policy is independent of the provider's discovery/mining settings.
public class WebSourceBatchConfigs
{
    public int BatchSize { get; set; } = 20;
    public int BatchSizeLimit { get; set; } = 20;
    public int BatchIterations { get; set; } = 1;
    public string BatchPeriodOffset { get; set; } = "00:00";
    public float BatchPeriodInMinutes { get; set; } = 60;
    public string ActiveHoursStart { get; set; } = "00:00";
    public string ActiveHoursEnd { get; set; } = "23:59";

    public void Validate()
    {
        if (BatchSize < 0 || BatchSizeLimit < 0 || BatchIterations < 0 ||
            float.IsNaN(BatchPeriodInMinutes) || float.IsInfinity(BatchPeriodInMinutes) || BatchPeriodInMinutes < 1f / 60f || BatchPeriodInMinutes > 525600)
            throw new ArgumentException("Web source batch sizes must be nonnegative and the period must be between one second and one year.");
        ParseTime(BatchPeriodOffset);
        ParseTime(ActiveHoursStart);
        ParseTime(ActiveHoursEnd);
    }

    private static void ParseTime(string value)
    {
        if (!TimeSpan.TryParse(value, out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            throw new ArgumentException($"Invalid web source time '{value}'; use HH:mm or HH:mm:ss within one day.");
    }

    public DateTime NextRun(DateTime now)
    {
        Validate();
        var first = now.Date + TimeSpan.Parse(BatchPeriodOffset);
        if (first > now) return first;
        var period = TimeSpan.FromMinutes(BatchPeriodInMinutes);
        return first.AddTicks(((now - first).Ticks / period.Ticks + 1) * period.Ticks);
    }

    public bool IsActive(DateTime now)
    {
        var start = TimeSpan.Parse(ActiveHoursStart);
        var end = TimeSpan.Parse(ActiveHoursEnd);
        var time = now.TimeOfDay;
        return start == end || (start < end ? time >= start && time < end : time >= start || time < end);
    }
}

public abstract class WebSourceConfigs : WebSourceBatchConfigs, IConfig
{
    public abstract string Type { get; }
    // If supplied, Batch replaces the legacy flat batch fields above.
    public WebSourceBatchConfigs Batch { get; set; }
    public bool Enabled { get; set; } = true;
    public bool EnablePitchGate { get; set; }
    public string PitchDiscordChannel { get; set; } = "#stream";
    public int PitchExpirationMinutes { get; set; } = 180;
    public int PitchApprovalScore { get; set; } = 1;
    public int PitchAutoApprovalBatchSize { get; set; }
    public int PitchMinimumVotesToQueue { get; set; } = 1;
    public int PitchMemoryMaxChars { get; set; } = 4000;
    public int PitchContinuityEpisodeLimit { get; set; } = 6;
    public int PitchContinuityMaxChars { get; set; } = 4000;
    public int PitchSourceSelftextMaxChars { get; set; } = 1200;
    public int PitchEvaluatorThreadMaxChars { get; set; } = 1800;
    public int PitchEvaluatorMemoryMaxChars { get; set; } = 1200;
    public int PitchEvaluatorContinuityMaxChars { get; set; } = 1200;
    public int PitchEvaluatorActorLimit { get; set; } = 40;
    public string LogoUrl { get; set; }
    public int? BrandColor { get; set; }
}
