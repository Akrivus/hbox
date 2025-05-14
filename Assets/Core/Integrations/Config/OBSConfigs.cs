using System;

public class OBSConfigs : IConfig
{
    public string Type => "obs";
    public string OBSWebSocketURI { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsRecording { get; set; }
    public bool DoSplitRecording { get; set; }
    public bool OnlyNewEpisodes { get; set; }
}