using System.Threading.Tasks;
using UnityEngine;

public class TitleCardGeneration : MonoBehaviour, ISubGenerator
{
    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var text = await LLM.CompleteAsync(await prompt.Resolve(chat.Log, chat.Characters, chat.Idea.Text), chat, true);
        chat.Title = text.Find("Title");
        chat.Synopsis = text.Find("Synopsis");
        chat.FileName += $"-{chat.Title.ToFileSafeString()}";
        return chat;
    }
}