using System.Collections.Generic;

public class RedditConfigs : WebSourceConfigs
{
    public override string Type => "reddit";
    public Dictionary<string, string> SubReddits { get; set; }
    public float MaxPostAgeInHours { get; set; } = 24;
    public int MaxDepth { get; set; } = 3;
    public int TopRoots { get; set; } = 3;
    public int TopLevelLimit { get; set; } = 30;
    public int PerLevelChildLimit { get; set; } = 20;
    public int MaxDialogueLines { get; set; } = 16;
    public int MaxCharsPerLine { get; set; } = 280;
    public string Sort { get; set; } = "confidence"; // or "top", "new"
    public float RequestSpacingSeconds { get; set; } = 6f;
    public float RateLimitCooldownSeconds { get; set; } = 300f;
    public int MaxRequestAttempts { get; set; } = 3;
    public string OAuthClientId { get; set; }
    public string OAuthClientSecret { get; set; }
    public string OAuthDeviceId { get; set; } = "DO_NOT_TRACK_THIS_DEVICE";
    public string OAuthUserAgent { get; set; } = "script:hbox:1.1 (by /u/Akrivus)";
}
