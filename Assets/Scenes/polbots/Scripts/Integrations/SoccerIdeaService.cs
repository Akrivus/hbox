using System.Threading.Tasks;

public sealed class SoccerIdeaService
{
    private readonly ChatGenerator generator;
    private readonly SoccerIdeaComposer composer;

    public SoccerIdeaService(ChatGenerator generator)
    {
        this.generator = generator;
        composer = new SoccerIdeaComposer(generator);
    }

    public void QueuePregameIdea(Actor homeActor, Actor awayActor, string matchId)
    {
        QueueIdea(composer.BuildPregameIdea(homeActor, awayActor, matchId));
    }

    public async Task QueuePostgameIdeas(
        Actor homeActor,
        Actor awayActor,
        string matchId,
        string score,
        string rawLog,
        string[] recentResidue,
        string rosterContext = "")
    {
        foreach (var idea in await composer.BuildPostgameIdeas(homeActor, awayActor, matchId, score, rawLog, recentResidue))
            QueueIdea(idea + "\n\nStory and on-field rosters:\n" + rosterContext);
    }

    private void QueueIdea(string ideaText)
    {
        if (string.IsNullOrWhiteSpace(ideaText))
            return;

        generator.AddIdeaToQueue(new Idea(ideaText));
    }
}
