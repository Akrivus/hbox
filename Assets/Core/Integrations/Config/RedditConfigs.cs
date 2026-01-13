using System.Collections.Generic;

public class RedditConfigs : IConfig
{
    public string Type => "reddit";
    public Dictionary<string, string> SubReddits { get; set; }
    public float MaxPostAgeInHours { get; set; }
    public int BatchMax { get; set; }
    public int BatchLifetimeMax { get; set; }
    public float BatchPeriodInMinutes { get; set; }
    public int MaxDepth { get; set; } = 3;
    public int TopRoots { get; set; } = 3;
    public int TopLevelLimit { get; set; } = 30;
    public int PerLevelChildLimit { get; set; } = 20;
    public int MaxDialogueLines { get; set; } = 16;
    public int MaxCharsPerLine { get; set; } = 280;
    public string Sort { get; set; } = "confidence"; // or "top", "new"
}