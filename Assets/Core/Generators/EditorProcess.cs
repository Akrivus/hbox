using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class EditorProcess : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool infer = true;

    [SerializeField]
    private bool fastMode = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        chat.Characters = string.Join("\n", chat.Actors
            .Select(actor => string.Format("#### {0} ({1})\n\n{2}\n",
                actor.Name,
                actor.Reference.Pronouns,
                actor.Context))
            .Distinct()
            .ToArray());
        if (infer)
            chat.Context = await LLM.CompleteAsync(
                await prompt.Resolve(
                    chat.Topic,
                    chat.Idea.Prompt,
                    chat.Characters),
                fastMode);
        else
            chat.Context = string.Format("{0}\n\n### Characters:\n{1}",
                chat.Topic,
                chat.Characters);
        return chat;
    }
}