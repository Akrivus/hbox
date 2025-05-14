using uLipSync;
using UnityEngine;

public class LipsController : AutoActor, ISubChats
{
    [SerializeField]
    private MeshRenderer lipsRenderer;

    [SerializeField]
    private uLipSyncTexture lipSync;

    private Sentiment sentiment;

    private void Update()
    {
        if (sentiment != ActorController.Sentiment && ActorController.Sentiment != null)
            UpdateLips();
    }

    private void UpdateLips()
    {
        lipSync.textures[0].texture = ActorController.Sentiment.Lips;
    }

    public void Initialize(Chat chat)
    {
        sentiment = Actor.DefaultSentiment;

        var context = chat.Actors.Get(Actor);
        if (context != null)
            sentiment = context.Sentiment;
        UpdateLips();
    }
}
