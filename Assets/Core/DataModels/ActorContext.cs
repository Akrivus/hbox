using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;

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