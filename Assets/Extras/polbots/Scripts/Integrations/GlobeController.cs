using System.Linq;
using UnityEngine;
using WPM;

public class GlobeController : MonoBehaviour
{
    public WorldMapGlobe Globe => WorldMapGlobe.instance;

    public ChatManager ChatManager;

    private Actor _lastActor;
    
    private void Start()
    {
        ChatManager.OnChatNodeActivated += OnChatNodeActivated;
    }

    private void OnChatNodeActivated(ChatNode node)
    {
        if (_lastActor == node.Actor)
            return;
        Globe.FlyToCountry(GetCountryIndex(node.Actor));
        _lastActor = node.Actor;
    }

    private int GetCountryIndex(Actor actor)
    {
        for (var i = 0; i < Globe.countries.Length; i++)
        {
            var aliases = actor.Neighbor == null ? actor.Aliases : actor.Neighbor.Aliases;
            if (aliases.Contains(Globe.countries[i].name))
                return i;
        }
        return -1;
    }
}
