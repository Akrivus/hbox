using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class NoiseTagger : MonoBehaviour, ISubGenerator
{
    public bool IsBlocking => false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var names = chat.Names;

        var soundGroups = await SelectSoundGroup(prompt, chat, names);
        foreach (var s in soundGroups)
            chat.Actors.Get(s.Key.Reference).SoundGroup = s.Value;

        return chat;
    }

    private async Task<Dictionary<ActorContext, string>> SelectSoundGroup(PromptResolver prompt, Chat chat, string[] names)
    {
        var options = string.Join(", ", GetSoundGroups(chat));
        var characters = string.Join("\n- ", names);
        var message = await LLM.CompleteAsync(await prompt.Resolve(options, characters, chat.Log), chat, true);

        var lines = message.Parse(names);

        return lines
            .Where(line => names.Contains(line.Key))
            .ToDictionary(
                line => chat.Actors.Get(line.Key),
                line => line.Value);
    }

    private string[] GetSoundGroups(Chat chat)
    {
		return Resources.LoadAll<SoundGroup>($"{chat.ManagerContext.Name}/SoundGroups")
			.Select(t => t.name)
			.ToArray();
	}
}
