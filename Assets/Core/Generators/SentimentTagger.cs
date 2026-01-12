using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class SentimentTagger : MonoBehaviour, ISubGenerator
{
    public virtual bool IsBlocking => false;

    [SerializeField]
    private bool doForNodes = true;

    public virtual void AfterTagging(string output, ChatNode node) { }

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var names = chat.Names;
        var tasks = new List<Task>();

        if (doForNodes)
            foreach (var node in chat.Nodes)
                if (node.Reactions == null || node.Reactions.Length == 0)
                    tasks.Add(GenerateForNode(prompt, chat, node, names));
        if (string.IsNullOrEmpty(chat.Context))
            tasks.Add(GenerateForChat(prompt, chat, names, chat.Topic));
        else
            tasks.Add(GenerateForChat(prompt, chat, names, chat.Context));
        await Task.WhenAll(tasks);
        return chat;
    }

    private async Task GenerateForChat(PromptResolver prompt, Chat chat, string[] names, string context)
    {
        var sentiment = await GetSentiment(prompt, chat, names, chat.Log, "Analyze initial conversation state based on context.", chat.Context, string.Empty);
        var reactions = ParseReactions(sentiment, names);
        foreach (var reaction in reactions)
            chat.Actors.Get(reaction.Actor).Sentiment = reaction.Sentiment;
    }

    public async Task GenerateForNode(PromptResolver prompt, Chat chat, ChatNode node, string[] names)
    {
        var actor = chat.Actors.Get(node.Actor);
        await actor.SetPrompt();

        var sentiment = await GetSentiment(prompt, chat, names, chat.Log, node.Line, actor.Context, actor.Prompt);
        node.Reactions = ParseReactions(sentiment, names);
        AfterTagging(sentiment, node);
    }

    private async Task<string> GetSentiment(PromptResolver prompt, Chat chat, string[] names, string transcript, string line, string context, string text)
    {
        var faces = "- " + string.Join("\n- ", chat.ManagerContext.Sentiments.Select(s => s.Name));
        var options = "- " + string.Join("\n- ", names);

        return await LLM.CompleteAsync(await prompt.Resolve(faces, options, transcript, line, context, text), chat, true);
    }

    private ChatNode.Reaction[] ParseReactions(string message, string[] names)
    {
        var lines = message.Parse(names);
        var reactions = new ChatNode.Reaction[lines.Count];
        var i = 0;

        foreach (var l in lines)
            if (TryParseReaction(l.Key, l.Value, out Actor actor, out Sentiment sentiment))
                reactions[i++] = new ChatNode.Reaction(actor, sentiment);
        return reactions.OfType<ChatNode.Reaction>().ToArray();
    }

    private bool TryParseReaction(string name, string text, out Actor actor, out Sentiment sentiment)
    {
        actor = ActorConverter.Convert(name);
        sentiment = SentimentConverter.Convert(text);
        return sentiment != null && actor != null;
    }
}