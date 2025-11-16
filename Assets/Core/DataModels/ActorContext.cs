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
    public bool HasPrompt => File.Exists($"./Vault/Prompts/Actors/{Name}.md");


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
            resolver = new PromptResolver("Defaults", "Actors");
            await resolver.Resolve(Reference.Name, Reference.Pronouns);
        }

        Prompt = resolver.Text;

        var names = actors.Select(a => a.Name).ToArray();
        foreach (var name in names)
        {
            var r = new PromptResolver("Actors", Name, "Codex", name);
            await r.Resolve();
            Prompt += r.Text + "\n\n";
        }
    }
}