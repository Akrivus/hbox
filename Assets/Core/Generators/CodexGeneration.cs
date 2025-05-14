using System.Threading.Tasks;
using UnityEditor.Analytics;
using UnityEngine;

public class CodexGeneration : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        foreach (var actor in chat.Actors)
        {
            await actor.SetPrompt();

            foreach (var other in chat.Actors)
            {
                await other.SetPrompt(chat.Actors);

                var resolver = new PromptResolver("Actors", actor.Name, "Codex", other.Name);
                await resolver.Resolve();

                var codex = await LLM.CompleteAsync(
                    await prompt.Resolve(chat.Log, resolver.Text, actor.Prompt, actor.Context, other.Name, actor.Name), fastMode);
            }
        }

        return chat;
    }
}