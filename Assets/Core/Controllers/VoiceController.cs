using System.Linq;
using UnityEngine;

public class VoiceController : AutoActor, ISubNode
{
    public void Activate(ChatNode node)
    {
        var reaction = node.Reactions.FirstOrDefault(r => r.Actor == Actor);
        var score = (reaction?.Sentiment.Score ?? 0f) / 20f;
        var pitch = score + 1f;

        ActorController.Voice.pitch = pitch * Actor.Pitch;
    }
}
