using System;
using UnityEngine;

public class ColorSchemeController : AutoActor, ISubChats
{
    [SerializeField]
    private ColorScheme[] colorSchemes;

    [SerializeField]
    private SkinnedMeshRenderer skin;

    public SkinnedMeshRenderer Skin { set => skin = value; }

    private void SetColorScheme(string colorScheme)
    {
        var found = Array.Find(colorSchemes, c => c.name == colorScheme);
        if (found != null)
            skin.material = found.material;
    }

    public void Initialize(Chat chat)
    {
        if (chat == null) return;
        var context = chat.Actors.Get(Actor);
        var costume = context.Costume;
        if (costume != null)
            SetColorScheme(costume);
    }

    [Serializable]
    public class ColorScheme
    {
        public string name;
        public Material material;
    }
}
