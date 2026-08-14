using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed class RomeCuriaService : IRomeVenueService
{
    public RomeVenue Venue => RomeVenue.Curia;

    public IReadOnlyList<RomeSpotlightCandidate> GenerateCandidates(RomeSpectacleState state, Actor[] actors)
    {
        var names = ResolveNames(actors);
        return new List<RomeSpotlightCandidate>
        {
            new RomeSpotlightCandidate
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                venue = Venue,
                proposerActor = names[0],
                faction = "Popularis",
                title = "Bread, Noise, and Divine Visibility",
                claimToAttention = $"{names[0]} claims the gods reward whoever can make the crowd feel fed, seen, and loudly Roman.",
                proposedScene = $"{names[0]} takes the lead in a public spectacle about grain anxiety becoming entertainment policy.",
                promisedMutation = "Public mood rises if the scene lands; treasury strains if it becomes too generous.",
                risk = "The Senate calls it bribery with better lighting.",
                support = 3,
                opposition = 1,
                wealth = 1,
                violence = 0,
                popularity = 3,
                chaos = 1
            },
            new RomeSpotlightCandidate
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                venue = Venue,
                proposerActor = names[1],
                faction = "Optimates",
                title = "Procedure Must Be Seen Winning",
                claimToAttention = $"{names[1]} insists Rome survives only if the gods see the Senate controlling the format.",
                proposedScene = $"{names[1]} leads a procedural trap where status, order, and public humiliation do the stabbing.",
                promisedMutation = "Senate mood improves; public mood may sour if the people feel excluded.",
                risk = "The audience mistakes procedure for cowardice.",
                support = 2,
                opposition = 2,
                wealth = 1,
                violence = 0,
                popularity = 1,
                chaos = 0
            },
            new RomeSpotlightCandidate
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                venue = Venue,
                proposerActor = names[2],
                faction = "Equestrian",
                title = "The Gods Respect Liquidity",
                claimToAttention = $"{names[2]} argues attention should flow toward whoever can pay for the next miracle.",
                proposedScene = $"{names[2]} leads a patronage spectacle where favors, invoices, and sacred accounting become weapons.",
                promisedMutation = "Treasury pressure eases, but scandal and dependency increase.",
                risk = "Everyone realizes the miracle has a sponsor.",
                support = 2,
                opposition = 1,
                wealth = 4,
                violence = 0,
                popularity = 1,
                chaos = 2
            }
        };
    }

    public Idea BuildDebateIdea(RomeSpectacleState state, IReadOnlyList<RomeSpotlightCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ROME SPECTACLE PHASE: Curia Debate");
        builder.AppendLine($"ARC: {state.arcTitle}");
        builder.AppendLine($"ACTIVE VENUE: {state.activeVenue}");
        builder.AppendLine($"CURRENT CRISIS: {state.activeCrisis}");
        builder.AppendLine();
        builder.AppendLine("The Curia has convened because Rome must decide who deserves the gods' attention next. Treat this like a Senate flame war with procedure, status, bribes, omens, and public performance all pretending to be law.");
        builder.AppendLine();
        builder.AppendLine("SPOTLIGHT CANDIDATES:");

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            builder.AppendLine($"{i + 1}. {candidate.title}");
            builder.AppendLine($"   Proposer: {candidate.proposerActor}");
            builder.AppendLine($"   Faction: {candidate.faction}");
            builder.AppendLine($"   Claim: {candidate.claimToAttention}");
            builder.AppendLine($"   Proposed lead scene: {candidate.proposedScene}");
            builder.AppendLine($"   Risk: {candidate.risk}");
        }

        builder.AppendLine();
        builder.AppendLine("Generate a full RomeBots scene where the candidates argue over which proposal should be played. The debate should end with the sense that one person has almost seized the camera, but do not resolve the winner mechanically in dialogue.");

        return new Idea(builder.ToString(), "RomeSpectacle", "RomeSpectacle", $"curia-debate-{DateTime.UtcNow:yyyyMMddHHmmss}");
    }

    public RomeSpotlightCandidate ResolveWinner(RomeSpectacleState state, IReadOnlyList<RomeSpotlightCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        var weighted = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Weight = Mathf.Max(1,
                    candidate.support -
                    candidate.opposition +
                    candidate.popularity +
                    candidate.wealth +
                    candidate.violence +
                    UnityEngine.Random.Range(0, Mathf.Max(1, candidate.chaos + state.chaos + 1)) +
                    Mathf.RoundToInt(state.publicMood * 0.5f) +
                    Mathf.RoundToInt(state.senateMood * 0.5f))
            })
            .ToList();

        var total = weighted.Sum(item => item.Weight);
        var roll = UnityEngine.Random.Range(0, total);
        foreach (var item in weighted)
        {
            roll -= item.Weight;
            if (roll < 0)
                return item.Candidate;
        }

        return weighted.Last().Candidate;
    }

    public RomePerformanceSlot BuildPerformanceSlot(RomeSpectacleState state, RomeSpotlightCandidate winner, IReadOnlyList<RomeSpotlightCandidate> candidates)
    {
        if (winner == null)
            return null;

        var supportingCast = candidates?
            .Where(candidate => candidate != null && candidate != winner)
            .Select(candidate => candidate.proposerActor)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToArray() ?? Array.Empty<string>();

        var slot = new RomePerformanceSlot
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 8),
            venue = winner.venue,
            leadActor = winner.proposerActor,
            authoritySource = "Curia resolution: votes, status, faction pressure, money, popularity, and chaos",
            scenePremise = winner.proposedScene,
            supportingCast = supportingCast,
            expectedMutation = winner.promisedMutation,
            unresolvedRisk = winner.risk
        };

        slot.stateMutations.Add($"{winner.proposerActor} gains the lead spotlight through the Curia.");
        slot.stateMutations.Add(winner.promisedMutation);
        if (!string.IsNullOrWhiteSpace(winner.risk))
            slot.stateMutations.Add($"Unresolved risk: {winner.risk}");

        return slot;
    }

    public Idea BuildLeadSceneIdea(RomeSpectacleState state, RomePerformanceSlot slot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ROME SPECTACLE PHASE: Lead Scene");
        builder.AppendLine($"ARC: {state.arcTitle}");
        builder.AppendLine($"VENUE THAT AWARDED SPOTLIGHT: {slot.venue}");
        builder.AppendLine($"LEAD ACTOR: {slot.leadActor}");
        builder.AppendLine($"AUTHORITY SOURCE: {slot.authoritySource}");
        builder.AppendLine();
        builder.AppendLine("The winner has been given a temporary claim on the gods' attention. This is their scene slot, not a permanent victory.");
        builder.AppendLine();
        builder.AppendLine($"SCENE PREMISE: {slot.scenePremise}");
        builder.AppendLine($"SUPPORTING CAST: {string.Join(", ", slot.supportingCast ?? Array.Empty<string>())}");
        builder.AppendLine($"EXPECTED MUTATION: {slot.expectedMutation}");
        builder.AppendLine($"UNRESOLVED RISK: {slot.unresolvedRisk}");
        builder.AppendLine();
        builder.AppendLine("Generate a RomeBots lead scene where the lead actor tries to convert attention into power. The scene should end with a concrete consequence that future Acta can record.");

        return new Idea(builder.ToString(), "RomeSpectacle", "RomeSpectacle", $"lead-scene-{DateTime.UtcNow:yyyyMMddHHmmss}");
    }

    private static string[] ResolveNames(Actor[] actors)
    {
        var names = actors?
            .Where(actor => actor != null && !string.IsNullOrWhiteSpace(actor.Name))
            .Select(actor => actor.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList() ?? new List<string>();

        foreach (var fallback in new[] { "Caesar", "Cicero", "Crassus" })
        {
            if (names.Count >= 3)
                break;
            if (!names.Contains(fallback))
                names.Add(fallback);
        }

        return names.Take(3).ToArray();
    }
}
