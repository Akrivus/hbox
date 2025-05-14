using UnityEngine;

public class EyesController : AutoActor, ISubChats
{
    [SerializeField]
    private MeshRenderer eyesRenderer;

    private Sentiment sentiment;

    private void Update()
    {
        if (sentiment != ActorController.Sentiment && ActorController.Sentiment != null)
            UpdateEyes();
    }

    private void UpdateEyes()
    {
        sentiment = ActorController.Sentiment;
        eyesRenderer.material.mainTexture = sentiment.Eyes;
    }

    public void Initialize(Chat chat)
    {
        sentiment = Actor.DefaultSentiment;

        var context = chat.Actors.Get(Actor);
        if (context != null)
            sentiment = context.Sentiment;
        UpdateEyes();
    }
}
