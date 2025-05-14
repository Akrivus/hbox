using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WrittenDialogue : MonoBehaviour, ISubGenerator.Sync
{
    public List<ChatLine> Lines;
    public List<ActorLine> Memories;

    [TextArea(2, 10)]
    public string Prompt;

    private void Awake()
    {
        GetComponent<ChatGenerator>()
            .AddIdeaToQueue(new Idea(Prompt));
    }

    public Chat Generate(PromptResolver prompt, Chat chat)
    {
        var nodes = new List<ChatNode>();
        foreach (var line in Lines)
            nodes.Add(new ChatNode
            {
                Actor = line.Actor,
                Text = line.Text,
                Line = line.Text,
                Say = line.Text.Scrub(),
                Actions = line.Text.Rinse(),
                Reactions = line.Reactions.Select(r => new ChatNode.Reaction
                {
                    Actor = r.Actor,
                    Sentiment = r.Sentiment
                }).ToArray()
            });
        chat.Nodes = nodes;
        return chat;
    }

    [Serializable]
    public class ChatLine
    {
        public Actor Actor;
        public Sentiment Sentiment;

        [TextArea(1, 10)]
        public string Text;

        public List<ReactLine> Reactions;
    }

    [Serializable]
    public class ReactLine
    {
        public Actor Actor;
        public Sentiment Sentiment;
    }

    [Serializable]
    public class ActorLine
    {
        public Actor Actor;

        [TextArea(2, 10)]
        public string Prompt;
    }
}