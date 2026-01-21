using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed class ChatManagerContext : MonoBehaviour
{
    public static ChatManagerContext Current => ChatManager.Instance.CurrentContext;

    public event Action OnChatQueueEmpty
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnChatQueueEmpty += _, _ => ChatManager.Instance.OnChatQueueEmpty -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnChatQueueEmpty -= _, value);
    }
    public event Action<Chat> OnChatQueueAdded
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnChatQueueAdded += _, _ => ChatManager.Instance.OnChatQueueAdded -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnChatQueueAdded -= _, value);
    }
    public event Action<Chat> OnChatLoaded
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnChatLoaded += _, _ => ChatManager.Instance.OnChatLoaded -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnChatLoaded -= _, value);
    }
    public event Func<Chat, IEnumerator> OnChatQueueTaken
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnChatQueueTaken += _, _ => ChatManager.Instance.OnChatQueueTaken -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnChatQueueTaken -= _, value);
    }
    public event Func<Chat, IEnumerator> OnIntermission
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnIntermission += _, _ => ChatManager.Instance.OnIntermission -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnIntermission -= _, value);
    }
    public event Action BeforeIntermission
    {
        add => Bindings.Bind(_ => ChatManager.Instance.BeforeIntermission += _, _ => ChatManager.Instance.BeforeIntermission -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.BeforeIntermission -= _, value);
    }
    public event Action<Chat> AfterIntermission
    {
        add => Bindings.Bind(_ => ChatManager.Instance.AfterIntermission += _, _ => ChatManager.Instance.AfterIntermission -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.AfterIntermission -= _, value);
    }
    public event Action<Chat, ActorController> OnActorAdded
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnActorAdded += _, _ => ChatManager.Instance.OnActorAdded -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnActorAdded -= _, value);
    }
    public event Action<Chat, ActorController> OnActorRemoved
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnActorRemoved += _, _ => ChatManager.Instance.OnActorRemoved -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnActorRemoved -= _, value);
    }
    public event Action<ChatNode> OnChatNodeActivated
    {
        add => Bindings.Bind(_ => ChatManager.Instance.OnChatNodeActivated += _, _ => ChatManager.Instance.OnChatNodeActivated -= _, value);
        remove => Bindings.Unbind(_ => ChatManager.Instance.OnChatNodeActivated -= _, value);
    }

    public string Name => name;
    public string Key => key;
    public string ScenePath => $"Scenes/{Name}";

    public Actor.SearchableList ActorsSearch { get; private set; }
    public Sentiment.SearchableList SentimentsSearch { get; private set; }
    public bool IsActive => ChatManager.Instance.Contexts.TryGetValue(Key, out var context) && context == this;

    [SerializeField]
    private string key;

    public ConfigManager ConfigManager;
    public AudioSource AudioSource;
    public SpawnPointManager[] SpawnPoints;
    public Transform[] FallbackSpawnPoints;

    public string[] Locations;
    public Actor[] Actors;
    public Sentiment[] Sentiments;

    public bool RemoveActorsOnCompletion = true;
    public bool DisableSoundEffects = false;

    private ChatManagerBinding Bindings;

    private void Awake()
    {
        ActorsSearch = new Actor.SearchableList(Actors.ToList());
        SentimentsSearch = new Sentiment.SearchableList(Sentiments.ToList());
        Locations = SpawnPoints.Select(s => s.name).ToArray();

        Bindings = new ChatManagerBinding();

        foreach (var actor in Actors)
            actor.ManagerContext = this;
    }

    private void OnDestroy()
    {
        Bindings.Dispose();
        AudioSource.Stop();
    }

    private IEnumerator Death()
    {
        yield return new WaitUntil(() => !IsActive && GetComponentInChildren<ChatGenerator>()?.IsActive != true);
        Destroy(gameObject);
    }

    public void MarkForDeath()
    {
        StartCoroutine(Death());
    }
}
