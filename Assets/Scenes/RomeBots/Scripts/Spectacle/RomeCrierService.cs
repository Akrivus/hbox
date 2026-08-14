using System;
using System.Linq;
using System.Text;

public sealed class RomeCrierService
{
    public string BuildTuneInRecap(RomeSpectacleState state)
    {
        if (state == null)
            return "ACTA DIURNA: Rome has misplaced its records and is pretending this was intentional.";

        var builder = new StringBuilder();
        builder.AppendLine("ACTA DIURNA");
        builder.AppendLine($"Arc: {state.arcTitle}");
        builder.AppendLine($"Venue: {state.activeVenue}");
        builder.AppendLine($"Current crisis: {state.activeCrisis}");
        builder.AppendLine($"Public mood: {state.publicMood}, Senate mood: {state.senateMood}, Treasury: {state.treasury}, Grain: {state.grain}, Chaos: {state.chaos}");

        if (!string.IsNullOrWhiteSpace(state.currentLeadActor))
            builder.AppendLine($"Recent lead: {state.currentLeadActor}");

        if (state.recentSpotlightWinners != null && state.recentSpotlightWinners.Count > 0)
            builder.AppendLine($"Recent spotlight: {string.Join(", ", state.recentSpotlightWinners.Skip(Math.Max(0, state.recentSpotlightWinners.Count - 5)))}");

        if (state.unresolvedHooks != null && state.unresolvedHooks.Count > 0)
            builder.AppendLine($"Unresolved: {string.Join("; ", state.unresolvedHooks.Take(3))}");

        return builder.ToString().Trim();
    }

    public RomeActaLog.ActaEntry WriteEntry(RomeSpectacleState state, RomeSpotlightCandidate winner, RomePerformanceSlot slot)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var crierText = BuildOutcomeRecap(state, winner, slot);
        var entry = RomeActaLog.CreateEntry(state, winner, slot, crierText);
        state.actaEntries.Add(entry);
        return entry;
    }

    private string BuildOutcomeRecap(RomeSpectacleState state, RomeSpotlightCandidate winner, RomePerformanceSlot slot)
    {
        var lead = slot?.leadActor ?? winner?.proposerActor ?? "Someone";
        var venue = slot?.venue ?? state.activeVenue;
        var mutation = slot?.stateMutations != null && slot.stateMutations.Count > 0
            ? string.Join("; ", slot.stateMutations)
            : "No official mutation was recorded, which is itself suspicious.";

        return $"The crier announces that {lead} has seized the {venue} spotlight. {mutation}";
    }
}
