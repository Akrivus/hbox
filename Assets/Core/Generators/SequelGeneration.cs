using System.Threading.Tasks;
using UnityEngine;

public class SequelGeneration : MonoBehaviour, ISubGenerator
{
    public bool IsBlocking => false;

    [SerializeField]
    private bool fastMode = false;

    [SerializeField]
    private ChatGenerator generator;

    private string slug => name.Replace(' ', '-').ToLower();

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var states = "";
        foreach (var actor in chat.Actors)
            states += $"#### {actor.Name}\n\n" + actor.Memory + "\n\n";
        var context = await MemoryBucket.GetContext(slug);
        var text = await LLM.CompleteAsync(
            await prompt.Resolve(context, states), chat, fastMode);
        generator.AddIdeaToQueue(new Idea(text));
        return chat;
    }
}