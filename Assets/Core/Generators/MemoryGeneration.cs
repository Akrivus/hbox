using System;
using System.Threading.Tasks;
using UnityEngine;

public class MemoryGeneration : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        foreach (var actor in chat.Actors)
        {
            await actor.SetPrompt();

            if (actor.HasPrompt)
                continue;

            var resolver = new PromptResolver("Actors", actor.Name, "Memories", DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss"));
            var bucket = await MemoryBucket.Get(actor.Name);
            var memory = await LLM.CompleteAsync(
                await prompt.Resolve(chat.Log, actor.Prompt, bucket.Get(), actor.Context), fastMode);
            actor.Memory = memory;
            await bucket.Add(prompt.Output);
        }

        return chat;
    }
}