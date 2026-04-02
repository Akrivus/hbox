using System;
using System.Collections.Generic;
using FStudio.Events;
using FStudio.MatchEngine;
using FStudio.MatchEngine.Events;
using UnityEngine;

public sealed class SoccerMatchStateService
{
    private readonly Queue<string> recentResidue = new Queue<string>();
    private readonly int residueLimit;

    private Actor homeActor;
    private Actor awayActor;

    public SoccerMatchStateService(int residueLimit = 8)
    {
        this.residueLimit = Mathf.Max(1, residueLimit);
        Phase = SoccerMatchPhase.Closed;
    }

    public string CurrentMatchId { get; private set; }
    public SoccerMatchPhase Phase { get; private set; }
    public long LatestSequence { get; private set; }

    public void BeginMatch(string matchId, Actor homeActor, Actor awayActor)
    {
        CurrentMatchId = matchId;
        this.homeActor = homeActor;
        this.awayActor = awayActor;
        Phase = SoccerMatchPhase.Pregame;
        LatestSequence = 0;
        recentResidue.Clear();
    }

    public void MarkLive()
    {
        if (!string.IsNullOrWhiteSpace(CurrentMatchId))
            Phase = SoccerMatchPhase.Live;
    }

    public void MarkPostgame()
    {
        if (!string.IsNullOrWhiteSpace(CurrentMatchId))
            Phase = SoccerMatchPhase.Postgame;
    }

    public void EndMatch()
    {
        CurrentMatchId = null;
        homeActor = null;
        awayActor = null;
        Phase = SoccerMatchPhase.Closed;
        LatestSequence = 0;
        recentResidue.Clear();
    }

    public void AppendResidue(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
            return;

        recentResidue.Enqueue(log);
        while (recentResidue.Count > residueLimit)
            recentResidue.Dequeue();
    }

    public string[] GetRecentResidue()
    {
        return recentResidue.ToArray();
    }

    public SoccerEventSummary BuildSummary(IBaseEvent e, string log, string primaryActor, bool highPriority)
    {
        var manager = MatchManager.Current;
        var homeScore = manager != null ? manager.homeTeamScore : 0;
        var awayScore = manager != null ? manager.awayTeamScore : 0;
        var minute = manager != null ? Mathf.CeilToInt(manager.minutes) : 0;
        var eventType = e.GetType().Name;
        var scoreSensitive = IsScoreSensitive(eventType);

        LatestSequence++;

        return new SoccerEventSummary
        {
            MatchId = CurrentMatchId,
            Phase = Phase,
            EventType = eventType,
            Minute = minute,
            HomeTeam = homeActor?.Name ?? "Home",
            AwayTeam = awayActor?.Name ?? "Away",
            HomeScore = homeScore,
            AwayScore = awayScore,
            PrimaryActor = primaryActor ?? string.Empty,
            RawLog = log,
            IsHighPriority = highPriority,
            Priority = GetPriority(eventType, highPriority),
            Sequence = LatestSequence,
            IsScoreSensitive = scoreSensitive,
            WorldFingerprint = BuildWorldFingerprint(eventType, minute, homeScore, awayScore, scoreSensitive),
            CreatedAtUtc = DateTime.UtcNow,
            RecentResidue = GetRecentResidue()
        };
    }

    public string BuildCurrentWorldFingerprint(string eventType, bool scoreSensitive)
    {
        var manager = MatchManager.Current;
        var minute = manager != null ? Mathf.CeilToInt(manager.minutes) : 0;
        var homeScore = manager != null ? manager.homeTeamScore : 0;
        var awayScore = manager != null ? manager.awayTeamScore : 0;
        return BuildWorldFingerprint(eventType, minute, homeScore, awayScore, scoreSensitive);
    }

    private static string BuildWorldFingerprint(string eventType, int minute, int homeScore, int awayScore, bool scoreSensitive)
    {
        if (!scoreSensitive)
            return $"{eventType}|generic";

        return $"{eventType}|{homeScore}-{awayScore}|{minute}";
    }

    private static bool IsScoreSensitive(string eventType)
    {
        return eventType == nameof(GoalScoredEvent);
    }

    private static SoccerInterruptPriority GetPriority(string eventType, bool highPriority)
    {
        if (highPriority)
            return SoccerInterruptPriority.High;

        return eventType switch
        {
            nameof(GoalScoredEvent) => SoccerInterruptPriority.High,
            nameof(RefereeShortWhistleEvent) => SoccerInterruptPriority.Normal,
            _ => SoccerInterruptPriority.Normal
        };
    }
}
