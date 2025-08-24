using System.Collections.Generic;

public class SoccerConfigs : IConfig
{
    public string Type => "soccer";
    public Dictionary<string, string[]> Lines { get; set; } = new Dictionary<string, string[]>();
    public int MatchTimeLimit { get; set; } = 10;
    public float TimeBetweenGames { get; set; } = 0;
    public float MaxVolume { get; set; }
    public bool ClearSceneOnGameEnd { get; set; }
    public bool RequireTextPatternMatch { get; set; }
    public bool GameOnStart { get; set; }
    public bool GameOnBatchEnd { get; set; }
    public bool GameOnMatchEnd { get; set; }
}