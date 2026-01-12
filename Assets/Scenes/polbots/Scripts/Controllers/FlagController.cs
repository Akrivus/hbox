using System.Collections;
using UnityEngine;

public class FlagController : AutoActor, ISubActor
{
    public Color Color => Actor.Color;

    [SerializeField]
    private MeshRenderer flagRenderer;
    private Texture2D flagTexture;

    private void Start()
    {
        ActorController.BeforeDestroy += ShrinkCharacterOutOfScreen;
        ActorController.AfterCreate += ScaleCharacterIntoScreen;
    }

    private IEnumerator ShrinkCharacterOutOfScreen()
    {
        var duration = 1f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale *= (duration - Time.deltaTime) / duration;
            yield return null;
        }
    }

    private IEnumerator ScaleCharacterIntoScreen()
    {
        var scale = transform.localScale;
        var duration = 1f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(Vector3.zero, scale, elapsed / duration);
            yield return null;
        }
    }

    private Texture2D LoadTexture(string name)
    {
        flagTexture = Resources.Load<Texture2D>($"{ChatManagerContext.Current.Name}/Flags/{name}");
        return flagTexture;
    }

    public void UpdateActor(ActorContext context)
    {
        flagRenderer.material.mainTexture = LoadTexture(context.Name);
    }
}
