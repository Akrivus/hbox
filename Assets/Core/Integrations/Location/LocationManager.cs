using System.Collections;
using System.Linq;
using UnityEngine;

public class LocationManager : MonoBehaviour
{
    public static LocationManager Instance => _instance ?? (_instance = FindFirstObjectByType<LocationManager>());
    private static LocationManager _instance;

    public LocationDefinition[] Locations;

    private LocationDefinition _location;

    private void Awake()
    {
        _instance = this;
    }

    private void Start()
    {
        ChatManagerContext.Current.OnChatQueueTaken += LoadLocation;
        ChatManagerContext.Current.OnActorAdded += UpdateSpawnPoint;
    }

    private IEnumerator LoadLocation(Chat chat)
    {
        if (_location != null)
            Destroy(_location);

        var location = Locations.FirstOrDefault(l => l.name == chat.Location);
        if (location == null)
            location = Locations[0];

        var @object = Instantiate(location.gameObject);
        yield return new WaitForSeconds(1);

        _location = @object.GetComponent<LocationDefinition>();
    }

    private void UpdateSpawnPoint(Chat chat, ActorController controller)
    {
        if (_location == null || _location.SpawnPoints == null)
            return;

        var a = chat.Actors.Get(controller.Actor.Name);
        if (a == null)
            return;
        var s = _location.SpawnPoints.FirstOrDefault(s => s != null && s.name == a.SpawnPoint);
        if (s == null)
            return;
        var t = controller.transform.parent = s;
        controller.transform.position = t.position;
        controller.transform.rotation = t.rotation;
    }
}
