using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class ActorContext
{
    [JsonConverter(typeof(ActorConverter))]
    public Actor Reference { get; set; }

    [JsonConverter(typeof(SentimentConverter))]
    public Sentiment Sentiment { get; set; }

    public string Costume { get; set; }
    public string Item { get; set; }
    public string SoundGroup { get; set; }
    public string SpawnPoint { get; set; }
    public string Context { get; set; }
    public string Memory { get; set; }

    [JsonIgnore]
    public string Prompt { get; private set; }

    [JsonIgnore]
    public string Name => Reference.Name;

    [JsonIgnore]
    public bool HasNoPrompt => !File.Exists($"./Vault/{Reference.ManagerContext.Key}/Prompts/Actors/{Name}.md");


    public ActorContext(Actor actor)
    {
        Reference = actor;
        Costume = actor.Costume;
    }

    public ActorContext()
    {

    }

    public async Task SetPrompt(params ActorContext[] actors)
    {
        var resolver = new PromptResolver(Reference);
        await resolver.Resolve();

        if (!resolver.Resolved)
        {
            resolver = new PromptResolver(Reference.ManagerContext, "Defaults", "Actors");
            await resolver.Resolve(Reference.Name, Reference.Pronouns);
        }

        Prompt = resolver.Text;

        var names = actors.Select(a => a.Name).ToArray();
        foreach (var name in names)
        {
            var r = new PromptResolver(Reference.ManagerContext, "Actors", Name, "Codex", name);
            await r.Nullable().Resolve();
            if (r.IsBlank)
                continue;
            Prompt += r.Text + "\n\n";
        }
    }
}