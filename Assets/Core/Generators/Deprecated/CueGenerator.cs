using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class CueGenerator : MonoBehaviour, ISubGenerator
{
    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var content = await LLM.CompleteAsync(await prompt.Resolve(chat.Topic), chat, true);

        var lines = content.Split('\n').Where(x => x.StartsWith("- ")).Select(x => x.Substring(2));
        chat.Cues = lines.ToArray();

        chat.EndingTrigger = content.Find("Ending Trigger");

        return chat;
    }
}