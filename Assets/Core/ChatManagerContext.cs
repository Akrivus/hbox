using System;
using System.Collections;
using System.Collections.Generic;
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

    public bool Dead { get; private set; } = false;

    public Actor.SearchableList ActorsSearch { get; private set; }
    public Sentiment.SearchableList SentimentsSearch { get; private set; }
    public bool IsActive => ChatManager.Instance.Contexts.TryGetValue(Key, out var context) && context == this;

    public SpawnPointManager[] ActiveSpawnPoints => ChatManager.Instance.Contexts.TryGetValue(Key, out var context) && context.SpawnPoints != null && context.SpawnPoints.Length > 0 ? context.SpawnPoints : SpawnPoints;
    public Transform[] ActiveFallbackSpawnPoints => ChatManager.Instance.Contexts.TryGetValue(Key, out var context) && context.FallbackSpawnPoints != null && context.FallbackSpawnPoints.Length > 0 ? context.FallbackSpawnPoints : FallbackSpawnPoints;

    [SerializeField]
    private string key;

    public ConfigManager ConfigManager;
    public AudioSource AudioSource;
    public SpawnPointManager[] SpawnPoints;
    public Transform[] FallbackSpawnPoints;

    public string[] Locations;
    public Actor[] Actors;
    public Sentiment[] Sentiments;

    public bool PostMemories = false;
    public bool RemoveActorsOnCompletion = true;
    public bool DisableSoundEffects = false;

    private ChatManagerBinding Bindings = new ChatManagerBinding();

    private void Awake()
    {
        var actorList = new List<Actor>(Actors?.Length ?? 0);
        if (Actors != null)
        {
            for (var i = 0; i < Actors.Length; i++)
                if (Actors[i] != null)
                    actorList.Add(Actors[i]);
        }

        var sentimentList = new List<Sentiment>(Sentiments?.Length ?? 0);
        if (Sentiments != null)
        {
            for (var i = 0; i < Sentiments.Length; i++)
                if (Sentiments[i] != null)
                    sentimentList.Add(Sentiments[i]);
        }

        var locations = new List<string>(SpawnPoints?.Length ?? 0);
        if (SpawnPoints != null)
        {
            for (var i = 0; i < SpawnPoints.Length; i++)
            {
                var spawnPoint = SpawnPoints[i];
                if (spawnPoint != null)
                    locations.Add(spawnPoint.name);
            }
        }

        ActorsSearch = new Actor.SearchableList(actorList);
        SentimentsSearch = new Sentiment.SearchableList(sentimentList);
        Locations = locations.ToArray();

        if (Actors == null)
            return;

        for (var i = 0; i < Actors.Length; i++)
        {
            var actor = Actors[i];
            if (actor != null)
                actor.ManagerContext = this;
        }
    }

    private void Start()
    {
        if (ChatManager.Instance == null)
        {
            if (Application.isEditor)
                Debug.LogError("hey dipshit you forgot to switch to the main scene. lol");
            Debug.LogWarning("ChatManagerContext scene without loading the Main scene first. Loading Main scene now...");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        }
    }

    private void OnDestroy()
    {
        Die();
    }

    private IEnumerator Death()
    {
        yield return new WaitUntil(() => !IsActive && GetComponentInChildren<ChatGenerator>()?.IsActive != true);
        Die();
    }

    public void MarkForDeath()
    {
        StartCoroutine(Death());
    }

    private void Die()
    {
        if (Dead) return;
        Dead = true;
        Bindings.Dispose();
        ChatManager.Instance?.UnregisterContext(this);
        if (AudioSource != null)
            AudioSource.Stop();
    }
}
