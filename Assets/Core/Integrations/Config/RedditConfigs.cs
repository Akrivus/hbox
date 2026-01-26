using System.Collections.Generic;

public class RedditConfigs : IConfig
{
    public string Type => "reddit";
    public Dictionary<string, string> SubReddits { get; set; }
    public float MaxPostAgeInHours { get; set; }
    public int BatchSize { get; set; }
    public int BatchSizeLimit { get; set; }
    public int BatchIterations { get; set; } = 1;
    public string BatchPeriodOffset { get; set; } = "00:00";
    public float BatchPeriodInMinutes { get; set; }
    public string ActiveWindowStart { get; set; } = "00:00";
    public string ActiveWindowEnd { get; set; } = "23:59";
    public int MaxDepth { get; set; } = 3;
    public int TopRoots { get; set; } = 3;
    public int TopLevelLimit { get; set; } = 30;
    public int PerLevelChildLimit { get; set; } = 20;
    public int MaxDialogueLines { get; set; } = 16;
    public int MaxCharsPerLine { get; set; } = 280;
    public string Sort { get; set; } = "confidence"; // or "top", "new"
}