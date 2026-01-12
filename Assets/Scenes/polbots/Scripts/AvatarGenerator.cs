using System.Collections;
using System.IO;
using UnityEngine;

public class AvatarGenerator : MonoBehaviour
{
    private Actor[] Actors;
    private Sentiment[] Sentiments;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Actors = ChatManagerContext.Current.Actors;
        Sentiments = ChatManagerContext.Current.Sentiments;
        StartCoroutine(GenerateAvatars());
    }

    // Update is called once per frame
    void Update()
    {

    }

    private IEnumerator GenerateAvatars()
    {
        foreach (var actor in Actors)
        {
            foreach (var sentiment in Sentiments)
            {
                var part = $"Vault/WWW/{ChatManagerContext.Current.Name}/{actor.Name}";
                var path = $"{part}/{sentiment.Name}.png";
                if (File.Exists(path))
                    continue;
                else if (!Directory.Exists(part))
                    Directory.CreateDirectory(part);

                var gameObject = Instantiate(actor.Prefab);
                var actorController = gameObject.GetComponent<ActorController>();
                actorController.Context = new ActorContext(actor);
                actorController.Sentiment = sentiment;

                yield return new WaitForEndOfFrame();

                var cameraObj = gameObject.transform.Find("Pivot").Find("Camera").gameObject;
                cameraObj.SetActive(true);

                var camera = cameraObj.GetComponent<Camera>();

                yield return new WaitForEndOfFrame();

                var texture = new Texture2D(256, 256);

                camera.targetTexture = new RenderTexture(256, 256, 24);
                camera.Render();
                RenderTexture.active = camera.targetTexture;
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                texture.Apply();

                var bytes = texture.EncodeToPNG();
                Destroy(texture);
                Destroy(gameObject);

                Debug.Log($"Generated avatar for {sentiment.Name}-{actor.Name}");

                if (!File.Exists(path))
                    File.Create(path).Dispose();
                File.WriteAllBytes(path, bytes);


            }
        }
    }

}
