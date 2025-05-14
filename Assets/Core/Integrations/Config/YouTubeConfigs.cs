using System;

public class YouTubeConfigs : IConfig
{
    public string Type => "youtube";
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public string[] Tags { get; set; }
}