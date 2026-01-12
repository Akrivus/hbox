using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class CodexGeneration : MonoBehaviour, ISubGenerator
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
        if (actor.HasPrompt)
            return;

        await actor.SetPrompt(chat.Actors);

        foreach (var other in chat.Actors)
        {
            await other.SetPrompt(chat.Actors);

            var resolver = new PromptResolver("Actors", actor.Name, "Codex", other.Name);
            await resolver.Resolve();

            var codex = await LLM.CompleteAsync(await prompt.Resolve(chat.Log, resolver.Text, actor.Prompt, actor.Context, other.Name, actor.Name), chat, fastMode);
        }
    }
}