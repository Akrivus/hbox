using System.Threading.Tasks;
using UnityEngine;

public class EpisodeToEpisodeContinuity : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    private string slug => "#" + name.Replace(' ', '-').ToLower();

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var bucket = await MemoryBucket.Get(slug);
        var memory = await LLM.CompleteAsync(
            await prompt.Resolve(chat.Log, bucket.Get(), chat.Idea.Prompt), fastMode);
        await bucket.Add(memory);

        return chat;
    }
}