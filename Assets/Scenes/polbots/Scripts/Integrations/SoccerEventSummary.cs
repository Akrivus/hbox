using System;

public sealed class SoccerEventSummary
{
    public string MatchId;
    public SoccerMatchPhase Phase;
    public string EventType;
    public int Minute;
    public string HomeTeam;
    public string AwayTeam;
    public int HomeScore;
    public int AwayScore;
    public string PrimaryActor;
    public string RawLog;
    public bool IsHighPriority;
    public SoccerInterruptPriority Priority;
    public long Sequence;
    public bool IsScoreSensitive;
    public string WorldFingerprint;
    public DateTime CreatedAtUtc;
    public string[] RecentResidue;

    public string ScoreLine => $"{HomeTeam} {HomeScore} - {AwayScore} {AwayTeam}";
}
