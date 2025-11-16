using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class DialogueEditorProcess : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private bool fastMode = false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        if (chat == null || chat.IsLocked)
            return chat;
        var content = await LLM.CompleteAsync(await prompt.Resolve(chat.Context, chat.OriginalLog, chat.Log, chat.Idea.Prompt), fastMode);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length != chat.Nodes.Count)
            Debug.LogWarning("Number of lines does not match number of nodes.");

        var max = Math.Min(lines.Length, chat.Nodes.Count);

        for (int i = 0; i < max; i++)
        {
            var line = lines[i];
            var parts = line.Split(':');
            if (parts.Length <= 1)
            {
                i--;
                continue;
            }

            var name = parts[0];
            var text = string.Join(":", parts.Skip(1));

            var node = chat.Nodes[i];
            node.SetText(text);
        }

        return chat;
    }
}