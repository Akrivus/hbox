using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class VibeSelector : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private AudioClip[] vibes;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var options = string.Join(", ", vibes.Select(vibe => vibe.name));

        try
        {
            var output = await LLM.CompleteAsync(await prompt.Resolve(options, chat.Log, chat.Topic));
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