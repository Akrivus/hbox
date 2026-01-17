using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class MemoryGeneration : MonoBehaviour, ISubGenerator
{
    public bool IsBlocking => false;

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
        if (actor.HasNoPrompt)
            return;
        await actor.SetPrompt();

        var resolver = new PromptResolver(chat.ManagerContext, "Actors", actor.Name, "Memories", DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss"));
        var bucket = await MemoryBucket.Get(chat.ManagerContext.Key, actor.Name);
        var memory = await LLM.CompleteAsync(
            await prompt.Resolve(chat.Log, actor.Prompt, bucket.Get(), actor.Context), chat, fastMode);
        actor.Memory = memory;
        await bucket.Add(prompt.Output);
    }
}