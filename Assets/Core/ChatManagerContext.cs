using System.Linq;
using UnityEngine;

public sealed class ChatManagerContext : MonoBehaviour
{
    public static ChatManagerContext Current => ChatManager.Instance.CurrentContext;

    public string Name => name;
    public string Key => key;

    public Actor.SearchableList ActorsSearch { get; private set; }
    public Sentiment.SearchableList SentimentsSearch { get; private set; }

    [SerializeField]
    private string key;

    public AudioSource AudioSource;
    public SpawnPointManager[] SpawnPoints;
    public Transform[] FallbackSpawnPoints;

    public string[] Locations;
    public Actor[] Actors;
    public Sentiment[] Sentiments;

    public bool RemoveActorsOnCompletion = true;
    public bool DisableSoundEffects = false;

    private void Awake()
    {
        ActorsSearch = new Actor.SearchableList(Actors.ToList());
        SentimentsSearch = new Sentiment.SearchableList(Sentiments.ToList());
        Locations = SpawnPoints.Select(s => s.name).ToArray();
    }
}
