using System.Threading.Tasks;
using UnityEngine;

public class EpisodeToEpisodeContinuity : MonoBehaviour, ISubGenerator
{
    public bool IsBlocking => false;

    [SerializeField]
    private bool fastMode = false;

    [SerializeField]
    private bool global = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var bucket = await MemoryBucket.Get(chat.ManagerContext, GetSlug(chat));
        var memory = await LLM.CompleteAsync(
            await prompt.Resolve(chat.Log, bucket.Get(), chat.Idea.Prompt), chat, fastMode);
        await bucket.Add(prompt.Output);

        return chat;
    }

    private string GetSlug(Chat chat)
    {
        return "#" + (global ? chat.ManagerContext.Name : name).Replace(' ', '-').ToLower();
    }
}