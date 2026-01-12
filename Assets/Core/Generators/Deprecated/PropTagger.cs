using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class PropTagger : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private string[] items;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        if (items == null || items.Length == 0)
            items = GetItemSet(chat);
        var names = chat.Names;
        var itemSet = await prompt.ExtractSet(chat, names, chat.Log)
            .ContinueWith(task => task.Result
                .ToDictionary(
                    line => chat.Actors.Get(line.Key),
                    line => line.Value));
        foreach (var item in itemSet)
            item.Key.Item = item.Value;
        return chat;
    }

    private string[] GetItemSet(Chat chat)
    {
        return Resources.LoadAll($"{chat.ManagerContext.Name}/Props", typeof(Texture2D))
                .Select(t => t.name)
                .ToArray();
    }
}
