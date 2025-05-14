using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class FlagController : AutoActor, ISubActor
{
    public Color Color => Actor.Color;

    [SerializeField]
    private MeshRenderer flagRenderer;
    private Texture2D flagTexture;

    private Texture2D LoadTexture(string name)
    {
        flagTexture = Resources.Load<Texture2D>($"{ChatManager.Instance.name}/Flags/{name}");
        return flagTexture;
    }

    public void UpdateActor(ActorContext context)
    {
        flagRenderer.material.mainTexture = LoadTexture(context.Name);
    }
}
