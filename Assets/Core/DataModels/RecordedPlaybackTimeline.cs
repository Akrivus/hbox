using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class RecordedPlaybackNode
{
    public int Sequence { get; set; }
    public double Offset { get; set; }
    public double MatchMinute { get; set; }
    public string SourceChat { get; set; }
    public string Kind { get; set; } = "node";
    public JObject Node { get; set; }
}

// Runtime session shared by the soccer director and normal chat playback.
public sealed class RecordedPlaybackTimeline
{
    public double? StartedAt { get; set; }
    public bool Finished { get; set; }
    public List<RecordedPlaybackNode> Nodes { get; set; }
    public Action<RecordedPlaybackNode> DispatchCue { get; set; }
}
