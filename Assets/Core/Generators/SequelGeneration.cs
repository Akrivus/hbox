using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class SequelGeneration : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    [SerializeField]
    private bool scramble = false;

    [SerializeField]
    private ChatGenerator[] iterations;

    private int iteration = 0;

    private string slug => name.Replace(' ', '-').ToLower();

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        if (chat.Idea.Prompt.Contains("[SEQUEL]") && iteration < iterations.Length)
        {
            var states = "";
            foreach (var actor in chat.Actors)
                states += $"#### {actor.Name}\n\n" + actor.Memory + "\n\n";
            if (scramble)
                iterations = iterations.Shuffle().ToArray();
            var generator = iterations[iteration % iterations.Length];
            var context = await MemoryBucket.GetContext(slug);
            var text = await LLM.CompleteAsync(
                await prompt.Resolve(context, states), fastMode);
            generator.AddPromptToQueue(text);
            iteration++;
        }
        else
        {
            iteration = 0;
        }
        return chat;
    }
}