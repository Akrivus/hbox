using System.Collections;
using System.IO;
using UnityEngine;

public class RBAvatarGenerator : MonoBehaviour
{
    [SerializeField]
    private Camera Camera;

    private Actor[] Actors;
    private Sentiment[] Sentiments;

    private bool initialUpdate = true;

    // Update is called once per frame
    void Update()
    {
        if (!initialUpdate) // unity wants to be a pain about two components using start at the same time, no LateStart, so this is the best way
            return;
        Actors = ChatManagerContext.Current.Actors;
        Sentiments = ChatManagerContext.Current.Sentiments;
        initialUpdate = false;
        StartCoroutine(GenerateAvatars());
    }

    private IEnumerator GenerateAvatars()
    {
        foreach (var actor in Actors)
        {
            foreach (var sentiment in Sentiments)
            {
                var path = $"Vault/WWW/{ChatManagerContext.Current.Name}/Actors/{actor.Name}/{sentiment.Name}.png";
                Debug.Log($"Checking {path} for {sentiment.Name}-{actor.Name}");
                if (File.Exists(path))
                    continue;
                else if (!Directory.Exists($"Vault/WWW/{ChatManagerContext.Current.Name}/Actors/{actor.Name}"))
                    Directory.CreateDirectory($"Vault/WWW/{ChatManagerContext.Current.Name}/Actors/{actor.Name}");

                var gameObject = Instantiate(actor.Prefab);
                var height = Vector3.up * gameObject.transform.localScale.y * 1.7f;
                Camera.transform.position += height;

                var actorController = gameObject.GetComponent<ActorController>();
                actorController.Context = new ActorContext(actor);
                actorController.Sentiment = sentiment;

                yield return new WaitForSeconds(1);

                var texture = new Texture2D(256, 256);

                Camera.targetTexture = new RenderTexture(256, 256, 24);
                Camera.Render();
                RenderTexture.active = Camera.targetTexture;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                texture.Apply();

                var bytes = texture.EncodeToPNG();
                Destroy(texture);
                Destroy(gameObject);

                Debug.Log($"Generated avatar for {sentiment.Name}-{actor.Name}");

                if (!File.Exists(path))
                    File.Create(path).Dispose();
                File.WriteAllBytes(path, bytes);

                Camera.transform.position -= height;
            }
        }
    }
}
