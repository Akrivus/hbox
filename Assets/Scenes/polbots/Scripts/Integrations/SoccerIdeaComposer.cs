using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class SoccerIdeaComposer
{
    private readonly ChatGenerator generator;

    public SoccerIdeaComposer(ChatGenerator generator)
    {
        this.generator = generator;
    }

    public string BuildPregameIdea(Actor homeActor, Actor awayActor, string matchId)
    {
        return ReadTemplate(
            "Pregame",
            ("{0}", matchId),
            ("{1}", homeActor?.Name),
            ("{2}", awayActor?.Name));
    }

    public async Task<List<string>> BuildPostgameIdeas(Actor homeActor, Actor awayActor, string matchId, string score, string rawLog, string[] recentResidue)
    {
        var ideas = new List<string>();
        var residueBlock = string.Join("\n- ", recentResidue ?? new string[0]);

        var mainFallout = await BuildMainFalloutIdea(homeActor, awayActor, matchId, score, rawLog, residueBlock);
        ideas.Add(mainFallout);

        if ((recentResidue ?? new string[0]).Any(line => line.ToLower().Contains("ref") || line.ToLower().Contains("whistle") || line.ToLower().Contains("tackle")))
        {
            ideas.Add(ReadTemplate(
                "Integrity Hearing",
                ("{0}", matchId),
                ("{1}", homeActor?.Name),
                ("{2}", awayActor?.Name),
                ("{3}", score),
                ("{4}", residueBlock)));
        }

        if (!string.IsNullOrWhiteSpace(score))
        {
            ideas.Add(ReadTemplate(
                "Rivalry Fallout",
                ("{0}", matchId),
                ("{1}", homeActor?.Name),
                ("{2}", awayActor?.Name),
                ("{3}", score)));
        }

        return ideas;
    }

    private async Task<string> BuildMainFalloutIdea(Actor homeActor, Actor awayActor, string matchId, string score, string rawLog, string residueBlock)
    {
        try
        {
            var highlightPrompt = new PromptResolver(generator.ManagerContext, "Soccer Mode", "Highlight Prompt");
            await highlightPrompt.Resolve(rawLog.Trim());

            return ReadTemplate(
                "Postgame",
                ("{0}", matchId),
                ("{1}", homeActor?.Name),
                ("{2}", awayActor?.Name),
                ("{3}", score),
                ("{4}", highlightPrompt.Text),
                ("{5}", residueBlock));
        }
        catch
        {
            return ReadTemplate(
                "Postgame",
                ("{0}", matchId),
                ("{1}", homeActor?.Name),
                ("{2}", awayActor?.Name),
                ("{3}", score),
                ("{4}", $"Raw match log:\n{rawLog}"),
                ("{5}", residueBlock));
        }
    }

    private string ReadTemplate(string templateName, params (string token, string value)[] replacements)
    {
        var resolver = new PromptResolver(generator.ManagerContext, "Soccer Mode", "Idea Seeds", templateName);
        var text = System.IO.File.Exists(resolver.Path) ? System.IO.File.ReadAllText(resolver.Path) : GetFallbackTemplate(templateName);

        foreach (var (token, value) in replacements)
            text = text.Replace(token, value ?? string.Empty);

        return text;
    }

    private static string GetFallbackTemplate(string templateName)
    {
        return templateName switch
        {
            "Pregame" =>
                "Pregame scene in polbots.\n\nMatchId: {0}\nHome: {1}\nAway: {2}\n\nGenerate a full-scene Idea about pregame nerves, alliance gossip, betting chatter, legitimacy concerns, and bureaucratic sports-pageantry before kickoff.",
            "Postgame" =>
                "Postgame fallout scene in polbots.\n\nMatchId: {0}\nTeams: {1} vs {2}\nScore: {3}\n\n{4}\n\nRecent residue:\n- {5}\n\nGenerate a full-scene Idea about postgame hearings, rivalry fallout, legitimacy disputes, sanctions, bets coming due, or bureaucratic spin after the match.",
            "Integrity Hearing" =>
                "Integrity hearing scene in polbots.\n\nMatchId: {0}\nTeams: {1} vs {2}\nScore: {3}\nTrigger residue:\n- {4}\n\nGenerate a full-scene Idea about UN complaints, Five Eyes officiating review, referee legitimacy arguments, and sanction threats after a controversial match.",
            "Rivalry Fallout" =>
                "Rivalry fallout scene in polbots.\n\nMatchId: {0}\nTeams: {1} vs {2}\nScore: {3}\n\nGenerate a full-scene Idea about how this result reopens old grievances, humiliations, and alliance tensions well beyond the sport itself.",
            _ => string.Empty
        };
    }
}
