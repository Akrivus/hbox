using System;
using FStudio.MatchEngine.Events;

public sealed class SoccerInterruptPolicy
{
    public SoccerPacketBank GetBank(SoccerEventSummary summary)
    {
        if (summary == null)
            return SoccerPacketBank.LiveGeneric;

        if (summary.Phase == SoccerMatchPhase.Pregame)
            return SoccerPacketBank.Pregame;

        if (summary.IsScoreSensitive || summary.Priority >= SoccerInterruptPriority.High)
            return SoccerPacketBank.LiveScoreSensitive;

        return SoccerPacketBank.LiveGeneric;
    }

    public int GetTargetCount(SoccerPacketBank bank, string eventType)
    {
        return bank switch
        {
            SoccerPacketBank.Pregame => 2,
            SoccerPacketBank.LiveScoreSensitive => 2,
            SoccerPacketBank.LiveGeneric => eventType == nameof(RefereeShortWhistleEvent) ? 2 : 3,
            SoccerPacketBank.Broadcast => 1,
            _ => 1
        };
    }

    public bool ShouldSupersede(SoccerInterruptPacket candidate, SoccerInterruptPacket existing)
    {
        if (candidate == null)
            return false;
        if (existing == null)
            return true;

        if (candidate.Priority > existing.Priority)
            return true;
        if (candidate.Priority < existing.Priority)
            return false;

        if (candidate.IsScoreSensitive && !existing.IsScoreSensitive)
            return true;
        if (!candidate.IsScoreSensitive && existing.IsScoreSensitive)
            return false;

        if (candidate.Sequence > existing.Sequence)
            return true;
        if (candidate.Sequence < existing.Sequence)
            return false;

        return candidate.CreatedAtUtc >= existing.CreatedAtUtc;
    }

    public bool IsPregameEvent(string eventType)
    {
        return string.Equals(eventType, "Pregame", StringComparison.Ordinal);
    }
}
