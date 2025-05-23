using System.Threading.Tasks;
using UnityEngine;

public class EpisodeToEpisodeContinuity : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    [SerializeField]
    private bool global = false;

    private string slug => "#" + (global ? ChatManager.Instance.name : name).Replace(' ', '-').ToLower();

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var bucket = await MemoryBucket.Get(slug);
        var memory = await LLM.CompleteAsync(
            await prompt.Resolve(chat.Log, bucket.Get(), chat.Idea.Prompt), fastMode);
        await bucket.Add(prompt.Output);

        return chat;
    }
}