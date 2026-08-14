using System.Collections.Generic;

public interface IRomeVenueService
{
    RomeVenue Venue { get; }
    IReadOnlyList<RomeSpotlightCandidate> GenerateCandidates(RomeSpectacleState state, Actor[] actors);
    Idea BuildDebateIdea(RomeSpectacleState state, IReadOnlyList<RomeSpotlightCandidate> candidates);
    RomeSpotlightCandidate ResolveWinner(RomeSpectacleState state, IReadOnlyList<RomeSpotlightCandidate> candidates);
    RomePerformanceSlot BuildPerformanceSlot(RomeSpectacleState state, RomeSpotlightCandidate winner, IReadOnlyList<RomeSpotlightCandidate> candidates);
    Idea BuildLeadSceneIdea(RomeSpectacleState state, RomePerformanceSlot slot);
}
