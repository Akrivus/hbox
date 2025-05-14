using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public class ActorNameMatcher : MonoBehaviour, ISubGenerator.Sync
{
    public Chat Generate(PromptResolver prompt, Chat chat)
    {
        var actors = new List<Actor>();
        foreach (var actor in ChatManager.Instance.Actors.List)
            foreach (var alias in actor.Aliases)
                if (Regex.IsMatch(chat.Topic, $@"\b{alias}(?: \(.+\))*(?:\*\*|:)"))
                    actors.Add(actor);
        actors.AddRange(chat.Nodes
            .Select(node => node.Actor)
            .Where(actor => !actors.Contains(actor)));

        chat.Actors = actors
            .Distinct()
            .OfType<Actor>()
            .Select(actor => new ActorContext(actor))
            .OfType<ActorContext>()
            .ToArray();
        return chat;
    }
}