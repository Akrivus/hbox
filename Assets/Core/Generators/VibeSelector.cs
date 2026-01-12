using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class VibeSelector : MonoBehaviour, ISubGenerator
{
    public bool IsBlocking => false;

    [SerializeField]
    private AudioClip[] vibes;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var options = string.Join(", ", vibes.Select(vibe => vibe.name));

        try
        {
            var output = await LLM.CompleteAsync(await prompt.Resolve(options, chat.Log, chat.Topic), chat);
            var vibe = vibes.FirstOrDefault(vibe => output.Contains(vibe.name)).name;

            chat.Vibe = vibe;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }

        return chat;
    }
}