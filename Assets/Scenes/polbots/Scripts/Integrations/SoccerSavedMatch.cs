using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Independent of Unity assets and chats. JSON payloads are immutable copies, not live references.
public sealed class SoccerMatchSetup
{
    public int Version { get; set; } = 1;
    public int Seed { get; set; }
    public string RandomAlgorithm { get; set; } = FStudio.MatchEngine.SoccerSimulationRandom.Algorithm;
    public string ChannelKey { get; set; }
    public string HomeActor { get; set; }
    public string AwayActor { get; set; }
    public string RosterContext { get; set; }
    public SoccerTeamSnapshot Home { get; set; }
    public SoccerTeamSnapshot Away { get; set; }
    public bool HomeAwayKit { get; set; }
    public bool AwayAwayKit { get; set; } = true;
    public int AiLevel { get; set; } = 4;
    public int DayTime { get; set; } = 2;
    public int MatchTimeLimit { get; set; }
    public string EngineSettingsJson { get; set; }
    public string UnityVersion { get; set; }
    public float FixedDeltaTime { get; set; }
    public float TimeScale { get; set; }

    public void Validate()
    {
        if (Version != 1 || RandomAlgorithm != FStudio.MatchEngine.SoccerSimulationRandom.Algorithm)
            throw new FormatException("Unsupported match setup or RNG version.");
        if (string.IsNullOrWhiteSpace(ChannelKey) || string.IsNullOrWhiteSpace(HomeActor) ||
            string.IsNullOrWhiteSpace(AwayActor) || HomeActor == AwayActor || Home == null || Away == null ||
            Home.Players?.Length != 11 || Away.Players?.Length != 11 || MatchTimeLimit <= 0 ||
            string.IsNullOrWhiteSpace(EngineSettingsJson) || string.IsNullOrWhiteSpace(UnityVersion) ||
            AiLevel < 0 || AiLevel > 4 || DayTime < 0 || DayTime > 2 ||
            float.IsNaN(FixedDeltaTime) || float.IsInfinity(FixedDeltaTime) || FixedDeltaTime <= 0 ||
            float.IsNaN(TimeScale) || float.IsInfinity(TimeScale) || TimeScale <= 0)
            throw new FormatException("Incomplete match setup.");
    }
}

public sealed class SoccerTeamSnapshot
{
    public string Template { get; set; }
    public string Name { get; set; }
    public int Formation { get; set; }
    public JObject[] Players { get; set; }
    public JObject HomeKit { get; set; }
    public JObject AwayKit { get; set; }
    public JObject Logo { get; set; }
}

public sealed class SoccerSavedMatch
{
    public string Id { get; set; }
    public string ReplayOf { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "preparing";
    public SoccerMatchSetup Setup { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public double Duration { get; set; }
    public List<RecordedPlaybackNode> Nodes { get; set; } = new List<RecordedPlaybackNode>();
}

public sealed class SoccerMatchStore
{
    private readonly string directory;
    public SoccerMatchStore(string directory) => this.directory = directory;
    public void Save(SoccerSavedMatch match)
    {
        SoccerEventStore.ValidateId(match.Id);
        match.Setup.Validate();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, match.Id + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(match));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
    public SoccerSavedMatch Load(string id)
    {
        SoccerEventStore.ValidateId(id);
        var match = JsonConvert.DeserializeObject<SoccerSavedMatch>(File.ReadAllText(Path.Combine(directory, id + ".json")));
        if (match == null || match.Id != id || match.Setup == null) throw new FormatException("Saved match identity/setup mismatch.");
        match.Setup.Validate();
        if (match.Nodes == null || double.IsNaN(match.Duration) || double.IsInfinity(match.Duration) || match.Duration < 0)
            throw new FormatException("Invalid saved timeline.");
        foreach (var entry in match.Nodes)
            if (entry == null || entry.Node == null || double.IsNaN(entry.Offset) || double.IsInfinity(entry.Offset) || entry.Offset < 0 ||
                (entry.Kind != "node" && entry.Kind != "announcer" && entry.Kind != "interrupt"))
                throw new FormatException("Invalid saved node.");
        return match;
    }
}

