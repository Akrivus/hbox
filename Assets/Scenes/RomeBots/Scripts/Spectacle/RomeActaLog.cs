using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[Serializable]
public sealed class RomeActaLog
{
    [Serializable]
    public sealed class ActaEntry
    {
        public string id;
        public string timestampUtc;
        public RomeVenue venue;
        public string phase;
        public string leadActor;
        public string winningClaim;
        public string outcome;
        public List<string> mutations = new List<string>();
        public List<string> unresolvedHooks = new List<string>();
        public string crierText;
    }

    public static ActaEntry CreateEntry(RomeSpectacleState state, RomeSpotlightCandidate winner, RomePerformanceSlot slot, string crierText)
    {
        return new ActaEntry
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 8),
            timestampUtc = DateTime.UtcNow.ToString("O"),
            venue = slot?.venue ?? state.activeVenue,
            phase = state.currentPhase,
            leadActor = slot?.leadActor ?? winner?.proposerActor ?? string.Empty,
            winningClaim = winner?.claimToAttention ?? string.Empty,
            outcome = slot?.scenePremise ?? string.Empty,
            mutations = slot?.stateMutations?.ToList() ?? new List<string>(),
            unresolvedHooks = state.unresolvedHooks?.Take(5).ToList() ?? new List<string>(),
            crierText = crierText ?? string.Empty
        };
    }

    public static string FormatRecent(RomeSpectacleState state, int limit = 5)
    {
        if (state?.actaEntries == null || state.actaEntries.Count == 0)
            return "No Acta entries yet.";

        var builder = new StringBuilder();
        foreach (var entry in state.actaEntries.Skip(Math.Max(0, state.actaEntries.Count - limit)))
        {
            builder.AppendLine($"- {entry.timestampUtc}: {entry.leadActor} seized the {entry.venue} spotlight.");
            if (!string.IsNullOrWhiteSpace(entry.winningClaim))
                builder.AppendLine($"  Claim: {entry.winningClaim}");
            if (!string.IsNullOrWhiteSpace(entry.outcome))
                builder.AppendLine($"  Outcome: {entry.outcome}");
            if (entry.mutations != null && entry.mutations.Count > 0)
                builder.AppendLine($"  Mutations: {string.Join("; ", entry.mutations)}");
        }

        return builder.ToString().Trim();
    }
}
