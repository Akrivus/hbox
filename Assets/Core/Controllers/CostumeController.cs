using System;
using UnityEngine;
using Utilities.Extensions;

public class CostumeController : AutoActor, ISubChats
{
    [SerializeField]
    private SkinnedMeshRenderer[] costumes;

    private ColorSchemeController _colorSchemeController;

    private void SetCostume(string costume)
    {
        foreach (var c in costumes)
            c.SetActive(false);
        var found = Array.Find(costumes, c => c.name == costume);
        if (found != null)
            found.SetActive(true);
        if (_colorSchemeController != null)
            _colorSchemeController.Skin = found;
    }

    public void Initialize(Chat chat)
    {
        if (chat == null) return;

        var context = chat.Actors.Get(Actor);
        var costume = context.Costume;

        _colorSchemeController = GetComponent<ColorSchemeController>();

        if (costume == null)
            costume = costumes.Random().name;
        if (costume != null)
            SetCostume(costume);
    }
}
