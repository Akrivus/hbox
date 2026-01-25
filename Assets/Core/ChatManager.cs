using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class ChatManager : MonoBehaviour
{
    public static ChatManager Instance { get; private set; }

    public static bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            if (_paused)
                Instance.OnPaused?.Invoke();
            else
                Instance.OnResumed?.Invoke();
        }
    }
    private static bool _paused;

    public static bool RepeatLastNode { get; set; }
    public static bool SkipToEnd { get; set; }

    public event Action OnChatQueueEmpty;
    public event Action<Chat> OnChatQueueAdded;
    public event Action<Chat> OnChatLoaded;

    public event Func<Chat, IEnumerator> OnChatQueueTaken;

    public event Func<Chat, IEnumerator> OnIntermission;

    public event Action BeforeIntermission;
    public event Action<Chat> AfterIntermission;

    public event Action<Chat, ActorController> OnActorAdded;
    public event Action<Chat, ActorController> OnActorRemoved;

    public event Action<ChatNode> OnChatNodeActivated;

    public event Action<ChatManagerContext> OnContextChanged;
    public event Action OnPaused;
    public event Action OnResumed;

    public Chat NowPlaying { get; private set; }
    public ChatManagerContext CurrentContext { get; private set; }
    public IDictionary<string, ChatManagerContext> Contexts => contexts;
    public ConcurrentQueue<Chat> PlayList => playList;
    public List<ActorController> ActorsInScene => actors;

    private readonly Dictionary<string, ChatManagerContext> contexts = new Dictionary<string, ChatManagerContext>();
    private List<ActorController> actors = new List<ActorController>();
    private ConcurrentQueue<Chat> playList = new ConcurrentQueue<Chat>();

    private SpawnPointManager spawnPointManager;
    private ChatNode lastNode;
    private float maxChance = 1f;

    [SerializeField]
    private EventSystem primaryEventSystem;

    [SerializeField]
    private bool forceSceneChange = true;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Instance = this;
        Cursor.visible = false;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        StartCoroutine(UpdatePlayList());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void AddToPlayList(Chat chat)
    {
        playList.Enqueue(chat);
        OnChatQueueAdded?.Invoke(chat);
    }

    private IEnumerator UpdatePlayList()
    {
        yield return new WaitUntil(() => CurrentContext != null);
        while (Application.isPlaying)
        {
            if (playList.IsEmpty) OnChatQueueEmpty?.Invoke();
            if (playList.TryDequeue(out var chat) && chat != null)
                yield return Play(chat);

            if (playList.IsEmpty || CurrentContext.RemoveActorsOnCompletion)
                yield return RemoveAllActors();

            SubtitleManager.Instance?.ClearSubtitles();
            yield return new WaitUntilTimer(() => !playList.IsEmpty, 30);
        }
    }

    private IEnumerator Play(Chat chat)
    {
        if (chat.IsLocked && chat.Nodes.Count < 2)
            yield break;

        if (chat.ManagerContext == null && contexts.TryGetValue(chat.Key, out var context))
            chat.ManagerContext = context;
        if (chat.ManagerContext != null)
            yield return SetCurrentContextAndChangeScene(chat.ManagerContext);
        if (chat.ManagerContext == null)
            chat.ManagerContext = ChatManagerContext.Current;

        if (StopPlaying(chat))
            yield break;

        if (OnChatQueueTaken != null)
            yield return OnChatQueueTaken(chat);

        PostChatTitleCard(chat);

        yield return InitChat(chat);
        yield return PlayChat(chat);

        PostChatActorMemories(chat);

        SkipToEnd = false;
    }

    private IEnumerator PlayChat(Chat chat)
    {
        yield return new WaitUntil(() => !IsPaused);

        if (chat.NextNode == null && !chat.IsLocked)
            yield return new WaitUntilTimer(() => chat.NextNode != null);

        if (RepeatLastNode)
        {
            lastNode.New = true;
            RepeatLastNode = false;
        }

        var node = chat.NextNode;
        if (node == null)
            yield break;
        lastNode = node;
        yield return Activate(node);

        node.New = false;

        yield return PlayChat(chat);
    }

    private IEnumerator InitChat(Chat chat)
    {
        if (spawnPointManager != null)
            spawnPointManager.UnRegister();
        lastNode = null;
        maxChance = 1f;
        if (CurrentContext.RemoveActorsOnCompletion)
            yield return RemoveAllActors();
        else
            yield return RemoveActors(chat);

        NowPlaying = chat;

        if (!string.IsNullOrEmpty(chat.Location))
            spawnPointManager = CurrentContext.SpawnPoints.FirstOrDefault(s => s.name == chat.Location);
        if (spawnPointManager == null)
            spawnPointManager = CurrentContext.SpawnPoints.Shuffle().FirstOrDefault();
        if (spawnPointManager != null)
            spawnPointManager.Register();

        BeforeIntermission?.Invoke();
        yield return SubtitleManager.Instance?.StartSplashScreen(chat);
        yield return OnIntermission?.Invoke(chat);

        OnChatLoaded?.Invoke(chat);

        var incoming = chat.Actors
            .Where(a => !actors.Select(ac => ac.Actor).Contains(a.Reference));

        foreach (var context in incoming)
            yield return AddActor(context);

        foreach (var ac in actors)
            if (chat.Actors.Select(a => a.Reference).Contains(ac.Actor))
                ac.Sentiment = chat.Actors.Get(ac.Actor).Sentiment;

        if (chat.IsLocked)
            foreach (var node in chat.Nodes)
                node.New = true;

        AfterIntermission?.Invoke(chat);
    }

    private IEnumerator Activate(ChatNode node)
    {
        if (SkipToEnd || StopPlaying(NowPlaying))
            yield break;

        DiscordManager.Instance?.SendDialogue(node);
        SubtitleManager.Instance?.OnNodeActivated(node);
        OnChatNodeActivated?.Invoke(node);

        var actor = actors.Get(node.Actor);
        if (actor == null)
            actor = actors.First();
        yield return actor.Activate(node);
        yield return SetActorReactions(actor, node);
    }

    private IEnumerator SetActorReactions(ActorController actor, ChatNode node)
    {
        try
        {
            var reactions = node.Reactions
                .Select(c => actors.FirstOrDefault(a => a.Actor == c.Actor))
                .ToDictionary(a => a, a => node.Reactions
                .First(r => r.Actor == a.Actor).Sentiment);
            foreach (var reaction in reactions)
            {
                reaction.Key.Sentiment = reaction.Value;
                reaction.Key.LookTarget = actor.LookObject;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error parsing reactions: {e}");
        }
        if (CurrentContext.DisableSoundEffects || CurrentContext.AudioSource == null)
            yield break;
        yield return PlayReactionClip(node.Reactions);
    }

    private IEnumerator PlayReactionClip(ChatNode.Reaction[] reactions)
    {
        var chance = UnityEngine.Random.Range(0f, maxChance);
        var reaction = reactions
            .GroupBy(r => r.Sentiment)
            .FirstOrDefault(r => r.Count() >= r.Key.MinReactions && chance <= r.Key.ReactionChance)
            ?.First()?.Sentiment;
        if (reaction == null)
            yield break;
        var clip = reaction.Sound;
        if (clip == null)
            yield break;
        maxChance *= reaction.ReactionDecay;
        CurrentContext.AudioSource.PlayOneShot(clip);
        yield return new WaitForSeconds(clip.length);
    }

    private IEnumerator AddActor(ActorContext context)
    {
        if (context == null)
            yield break;

        var spawnPointTransform = CurrentContext.FallbackSpawnPoints.FirstOrDefault(t => t.transform.childCount == 0);

        if (spawnPointManager != null)
        {
            var spawnPoint = spawnPointManager.spawnPoints.FirstOrDefault(t => t.name == context.Name);
            if (spawnPoint == null)
                spawnPoint = spawnPointManager.spawnPoints.FirstOrDefault(t => t.transform.childCount == 0);
            if (spawnPoint != null)
                spawnPointTransform = spawnPoint.transform;
        }

        var obj = Instantiate(context.Reference.Prefab, spawnPointTransform);

        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        var controller = obj.GetComponent<ActorController>();
        controller.Context = context;
        controller.Sentiment = context.Reference.DefaultSentiment;

        actors.Add(controller);
        yield return controller.Initialize(NowPlaying);
        OnActorAdded?.Invoke(NowPlaying, controller);
    }

    private IEnumerator RemoveActors(Chat chat)
    {
        var outgoing = actors.Except(chat.Actors.Select(ac => actors.Get(ac.Reference))).ToList();
        foreach (var actor in outgoing)
            yield return RemoveActor(actor);
    }

    public IEnumerator RemoveAllActors()
    {
        var outgoing = actors.ToArray();
        foreach (var actor in outgoing)
            yield return RemoveActor(actor);
    }

    private IEnumerator RemoveActor(ActorController controller)
    {
        yield return controller?.Deactivate();
        actors.Remove(controller);
        OnActorRemoved?.Invoke(NowPlaying, controller);
    }

    public void ResetContext()
    {
        if (CurrentContext != null)
            CurrentContext.MarkForDeath();
        CurrentContext = null;
    }

    public bool SetCurrentContext(ChatManagerContext context)
    {
        if (context == null || context.IsActive)
            return false;
        if (contexts.TryGetValue(context.Key, out var staleContext))
            staleContext.MarkForDeath();
        contexts[context.Key] = context;
        CurrentContext = context;
        DontDestroyOnLoad(context.gameObject);
        OnContextChanged?.Invoke(context);
        return true;
    }

    public IEnumerator SetCurrentContextAndChangeScene(ChatManagerContext context)
    {
        if (SetCurrentContext(context))
            yield return ChangeScene();
    }

    private IEnumerator ChangeScene()
    {
        var async = SceneManager.LoadSceneAsync(CurrentContext.ScenePath);
        yield return new WaitUntil(() => async.isDone);
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!primaryEventSystem)
            primaryEventSystem = FindFirstObjectByType<EventSystem>();
        foreach (var go in s.GetRootGameObjects())
        {
            var context = go.GetComponentInChildren<ChatManagerContext>();
            if (context)
                Instance.SetCurrentContext(context);
            var systems = go.GetComponentInChildren<EventSystem>();
            if (systems && systems != primaryEventSystem)
                Destroy(systems.gameObject);
        }
    }

    private bool StopPlaying(Chat chat)
    {
        return chat.ManagerContext == null || chat.ManagerContext.Key != CurrentContext.Key;
    }

    private void PostChatActorMemories(Chat chat)
    {
        if (StopPlaying(chat))
            return;
        foreach (var actor in chat.Actors)
        {
            if (string.IsNullOrEmpty(actor.Memory))
                continue;
            DiscordManager.PutInQueue("#stream", new DiscordWebhookMessage(
                string.Empty, null, null,
                new DiscordEmbed
                {
                    Title = $"{actor.Costume} {actor.Name}",
                    Description = actor.Memory,
                    Color = actor.Reference.Color1.ToDiscordColor()
                }));
        }
    }

    private void PostChatTitleCard(Chat chat)
    {
        if (StopPlaying(chat) || chat.Title == null)
            return;
        DiscordManager.PutInQueue("#stream", new DiscordWebhookMessage(
            "# :clapper: Now Streaming!", null, null,
            new DiscordEmbed
            {
                Title = chat.Title,
                Description = chat.Synopsis
            }));
    }
}