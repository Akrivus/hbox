using System.Threading.Tasks;
using UnityEngine;

public class BehaviorGeneration : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        foreach (var actor in chat.Actors)
        {
            await actor.SetPrompt(chat.Actors);

            var bucket = await MemoryBucket.Get(actor.Name);
            actor.Context = await LLM.CompleteAsync(
                await prompt.Resolve(
                    chat.Topic,
                    chat.Idea.Prompt,
                    actor.Prompt,
                    bucket.Get()),
                fastMode);
        }
        return chat;
    }
}