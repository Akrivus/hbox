using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class BehaviorGeneration : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var tasks = new List<Task>();
        foreach (var actor in chat.Actors)
            tasks.Add(GenerateForActor(prompt, chat, actor));
        await Task.WhenAll(tasks);
        return chat;
    }

    private async Task GenerateForActor(PromptResolver prompt, Chat chat, ActorContext actor)
    {
        if (actor.HasPrompt)
            return;

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
}