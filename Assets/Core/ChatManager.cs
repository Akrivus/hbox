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
    private const float MemorySnapshotHeartbeatSeconds = 15f;

    public static bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            if (_paused)
                Instance?.SafeInvoke(Instance.OnPaused, nameof(OnPaused));
            else
                Instance?.SafeInvoke(Instance.OnResumed, nameof(OnResumed));
        }
    }
    private static bool _paused;

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
    public bool ReadyForAction { get; set; }
    public IDictionary<string, ChatManagerContext> Contexts => contexts;
    public ConcurrentQueue<Chat> PlayList => playList;
    public List<ActorController> ActorsInScene => actors;

    public string ResetScenePath = "Reset";

    private readonly Dictionary<string, ChatManagerContext> contexts = new Dictionary<string, ChatManagerContext>();
    private List<ActorController> actors = new List<ActorController>();
    private ConcurrentQueue<Chat> playList = new ConcurrentQueue<Chat>();

    private SpawnPointManager spawnPointManager;
    private float maxChance = 1f;

    private bool readyToPlay = false;
    private int playbackGeneration = 0;
    private DiscordPostedMessage nowPlayingDiscordMessage;
    private bool lastPlayInterrupted = false;

    [SerializeField]
    private EventSystem primaryEventSystem;

    private void OnEnable()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        OperatorTelemetry.CaptureMemorySnapshot("chat_manager_started");
        StartCoroutine(UpdatePlayList());
        StartCoroutine(CaptureMemorySnapshotHeartbeat());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void AddToPlayList(Chat chat)
    {
        playList.Enqueue(chat);
        OperatorTelemetry.RecordEpisodeQueued(chat);
        OperatorTelemetry.CaptureMemorySnapshot("episode_queued");
        SafeInvoke(OnChatQueueAdded, chat, nameof(OnChatQueueAdded));
        readyToPlay = false;
    }

    public bool InjectNodes(Chat chat, IEnumerable<ChatNode> nodes)
    {
        if (chat == null || nodes == null)
            return false;
        if (chat != NowPlaying || StopPlaying(chat))
            return false;

        var injected = new List<ChatNode>();
        foreach (var node in nodes)
        {
            if (node != null)
                injected.Add(node);
        }

        if (injected.Count == 0)
            return false;

        foreach (var node in injected)
            node.New = true;

        var insertAt = chat.Nodes.FindIndex(node => node.New);
        if (insertAt < 0)
            insertAt = chat.Nodes.Count;

        chat.Nodes.InsertRange(insertAt, injected);
        return true;
    }

    private IEnumerator UpdatePlayList()
    {
        while (this != null)
        {
            yield return new WaitUntil(() => ReadyForAction);

            if (playList.TryDequeue(out var chat) && chat != null)
            {
                if (!chat.NewEpisode && chat.Key != CurrentContext?.Key)
                {
                    readyToPlay = true;
                    continue;
                }
                yield return RunCoroutineSafely(Play(chat), "Play", () => chat == null || this == null);
                SubtitleManager.Instance?.ClearSubtitles();
                readyToPlay = !lastPlayInterrupted;
            }
            else if (readyToPlay)
            {
                SafeInvoke(OnChatQueueEmpty, nameof(OnChatQueueEmpty));
                readyToPlay = false;
            }
            yield return new WaitUntil(() => !IsPaused);
        }
    }

    private IEnumerator CaptureMemorySnapshotHeartbeat()
    {
        while (this != null)
        {
            yield return new WaitForSecondsRealtime(MemorySnapshotHeartbeatSeconds);
            OperatorTelemetry.CaptureMemorySnapshot("heartbeat");
        }
    }

    private IEnumerator Play(Chat chat)
    {
        yield return new WaitUntil(() => NowPlaying == null);

        var completed = false;
        lastPlayInterrupted = false;

        try
        {
            var expectedKey = chat?.ManagerContext?.Key ?? chat?.Key;

            if (chat.IsLocked && chat.Nodes.Count < 2)
                yield break;

            if (contexts.TryGetValue(chat.Key, out var context))
                if (chat.ManagerContext == null)
                    chat.ManagerContext = context;
            if (chat.ManagerContext != null && chat.NewEpisode)
                yield return SetCurrentContextAndChangeScene(chat.ManagerContext);

            if (contexts.TryGetValue(chat.Key, out context) && context != null)
                chat.ManagerContext = context;

            expectedKey = chat?.ManagerContext?.Key ?? expectedKey;
            var generation = playbackGeneration;

            if (!IsPlaybackCurrent(chat, expectedKey, generation) || StopPlaying(chat))
                yield break;

            yield return RunEventCoroutines(OnChatQueueTaken, chat, generation, expectedKey, nameof(OnChatQueueTaken));

            if (!IsPlaybackCurrent(chat, expectedKey, generation))
                yield break;

            PostChatTitleCard(chat);

            yield return InitChat(chat, generation, expectedKey);
            if (!IsPlaybackCurrent(chat, expectedKey, generation))
                yield break;

            yield return PlayChat(chat, generation, expectedKey);
            if (!IsPlaybackCurrent(chat, expectedKey, generation))
                yield break;

            if (!SkipToEnd || chat.ManagerContext.PostMemories)
                PostChatActorMemories(chat);

            completed = true;
        }
        finally
        {
            SkipToEnd = false;
            lastPlayInterrupted = !completed;
            ReleaseActorVoiceClips();
            chat?.ReleaseRuntimeAudio();
            UnpinNowPlayingMessage(chat);
            if (NowPlaying == chat)
                NowPlaying = null;

            if (!completed && spawnPointManager != null && CurrentContext?.Key != chat?.ManagerContext?.Key)
                spawnPointManager.UnRegister();
        }
    }

    private IEnumerator PlayChat(Chat chat, int generation, string expectedKey)
    {
        yield return new WaitUntil(() => !IsPaused);

        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        if (chat.NextNode == null && !chat.IsLocked)
            yield return new WaitUntilTimer(() => chat.NextNode != null);

        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        var node = chat.NextNode;
        if (node == null)
            yield break;
        yield return Activate(chat, node, generation, expectedKey);

        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        node.New = false;

        yield return PlayChat(chat, generation, expectedKey);
    }

    private IEnumerator InitChat(Chat chat, int generation, string expectedKey)
    {
        yield return RecoverLiveContext(chat, expectedKey, generation);
        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        var context = ResolveLiveContext(chat, expectedKey) ?? chat?.ManagerContext;
        if (context == null)
        {
            Debug.LogWarning("ChatManager.InitChat skipped because chat.ManagerContext is null.");
            yield break;
        }

        chat.ManagerContext = context;

        if (spawnPointManager != null)
            spawnPointManager.UnRegister();
        spawnPointManager = null;
        maxChance = 1f;
        if (context.RemoveActorsOnCompletion)
            yield return RemoveAllActors();
        else
            yield return RemoveActors(chat);

        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        NowPlaying = chat;
        OperatorTelemetry.CaptureMemorySnapshot("episode_started");

        var activeSpawnPoints = GetReadySpawnPointManagers(context);
        if (!string.IsNullOrEmpty(chat.Location))
            spawnPointManager = activeSpawnPoints.FirstOrDefault(s => s != null && s.name == chat.Location);
        if (spawnPointManager == null)
            spawnPointManager = activeSpawnPoints.Where(s => s != null).Shuffle().FirstOrDefault();
        if (spawnPointManager != null)
            spawnPointManager.Register();

        SafeInvoke(BeforeIntermission, nameof(BeforeIntermission));
        yield return SubtitleManager.Instance?.StartSplashScreen(chat);
        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        yield return RunEventCoroutines(OnIntermission, chat, generation, expectedKey, nameof(OnIntermission));
        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        OperatorTelemetry.RecordEpisodePlaying(chat);
        SafeInvoke(OnChatLoaded, chat, nameof(OnChatLoaded));

        var chatActors = chat.Actors ?? Array.Empty<ActorContext>();
        var fallbackSpawnPoints = GetValidFallbackSpawnPoints(chat.ManagerContext);
        var activeActorReferences = new HashSet<Actor>();
        foreach (var actorController in actors)
        {
            if (actorController?.Actor != null)
                activeActorReferences.Add(actorController.Actor);
        }

        foreach (var actor in chatActors)
        {
            if (actor?.Reference == null || activeActorReferences.Contains(actor.Reference))
                continue;

            yield return AddActor(actor, GetFirstAvailableFallbackSpawnPoint(fallbackSpawnPoints));
            if (!IsPlaybackCurrent(chat, expectedKey, generation))
                yield break;
        }

        var sentimentsByActor = new Dictionary<Actor, Sentiment>();
        foreach (var chatActor in chatActors)
        {
            if (chatActor?.Reference != null)
                sentimentsByActor[chatActor.Reference] = chatActor.Sentiment;
        }

        foreach (var ac in actors)
        {
            if (ac?.Actor != null && sentimentsByActor.TryGetValue(ac.Actor, out var sentiment) && sentiment != null)
                ac.Sentiment = sentiment;
        }

        if (chat.IsLocked)
        {
            var nodes = chat.Nodes;
            if (nodes != null)
                for (var i = 0; i < nodes.Count; i++)
                    if (nodes[i] != null)
                        nodes[i].New = true;
        }

        SafeInvoke(AfterIntermission, chat, nameof(AfterIntermission));
    }

    private IEnumerator Activate(Chat chat, ChatNode node, int generation, string expectedKey)
    {
        if (node == null || SkipToEnd || !IsPlaybackCurrent(chat, expectedKey, generation) || StopPlaying(chat))
            yield break;

        DiscordManager.Instance?.SendDialogue(node);
        SubtitleManager.Instance?.OnNodeActivated(node);
        SafeInvoke(OnChatNodeActivated, node, nameof(OnChatNodeActivated));

        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;

        var actor = actors.Get(node.Actor);
        if (actor == null)
            actor = actors.Count > 0 ? actors[0] : null;
        if (actor == null)
        {
            Debug.LogWarning($"ChatManager.Activate skipped because no actor controller is available for '{node.Actor?.Name ?? "unknown"}'.");
            yield break;
        }
        yield return actor.Activate(node);
        if (!IsPlaybackCurrent(chat, expectedKey, generation))
            yield break;
        yield return SetActorReactions(actor, node);
    }

    private IEnumerator SetActorReactions(ActorController actor, ChatNode node)
    {
        if (actor == null || node == null)
            yield break;

        var reactions = node.Reactions ?? Array.Empty<ChatNode.Reaction>();
        try
        {
            var sentimentsByController = new Dictionary<ActorController, Sentiment>();
            foreach (var reaction in reactions)
            {
                if (reaction?.Actor == null)
                    continue;

                var controller = actors.Get(reaction.Actor);
                if (controller == null || reaction.Sentiment == null)
                    continue;

                sentimentsByController[controller] = reaction.Sentiment;
            }

            foreach (var reaction in sentimentsByController)
            {
                reaction.Key.Sentiment = reaction.Value;
                reaction.Key.LookTarget = actor.LookObject;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error parsing reactions: {e}");
        }
        var context = CurrentContext;
        if (context == null || context.DisableSoundEffects || context.AudioSource == null || reactions.Length == 0)
            yield break;
        yield return PlayReactionClip(reactions, context);
    }

    private IEnumerator PlayReactionClip(ChatNode.Reaction[] reactions, ChatManagerContext context)
    {
        if (reactions == null || reactions.Length == 0 || context?.AudioSource == null)
            yield break;

        var chance = UnityEngine.Random.Range(0f, maxChance);
        var reactionCounts = new Dictionary<Sentiment, int>();
        Sentiment reaction = null;
        for (var i = 0; i < reactions.Length; i++)
        {
            var sentiment = reactions[i]?.Sentiment;
            if (sentiment == null)
                continue;

            reactionCounts.TryGetValue(sentiment, out var count);
            count++;
            reactionCounts[sentiment] = count;

            if (reaction == null && count >= sentiment.MinReactions && chance <= sentiment.ReactionChance)
                reaction = sentiment;
        }

        if (reaction == null)
            yield break;
        var clip = reaction.Sound;
        if (clip == null)
            yield break;
        maxChance *= reaction.ReactionDecay;
        context.AudioSource.PlayOneShot(clip);
        yield return new WaitForSeconds(clip.length);
    }

    private IEnumerator AddActor(ActorContext context, Transform spawnPointTransform)
    {
        if (context == null || context.Reference == null) // another weird fluke
            yield break;
        if (context.Reference.Prefab == null)
        {
            Debug.LogWarning($"ChatManager.AddActor skipped because prefab is missing for actor '{context.Name}'.");
            yield break;
        }

        if (spawnPointManager != null)
        {
            var spawnPoints = spawnPointManager.spawnPoints ?? Array.Empty<SpawnPointManager.SpawnPoint>();
            var spawnPoint = spawnPoints.FirstOrDefault(t => t != null && t.name == context.Name);
            if (spawnPoint == null)
                spawnPoint = spawnPoints.FirstOrDefault(t => t != null && t.transform.childCount == 0);
            if (spawnPoint != null)
                spawnPointTransform = spawnPoint.transform;
        }

        var obj = spawnPointTransform != null
            ? Instantiate(context.Reference.Prefab, spawnPointTransform)
            : Instantiate(context.Reference.Prefab);
        if (obj == null)
        {
            Debug.LogWarning($"ChatManager.AddActor failed to instantiate actor '{context.Name}'.");
            yield break;
        }

        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        var controller = obj.GetComponent<ActorController>();
        if (controller == null)
        {
            Debug.LogWarning($"ChatManager.AddActor skipped because prefab for '{context.Name}' has no ActorController.");
            Destroy(obj);
            yield break;
        }
        controller.Context = context;
        controller.Sentiment = context.Reference.DefaultSentiment;

        actors.Add(controller);

        yield return controller.Initialize(NowPlaying);
        SafeInvoke(OnActorAdded, NowPlaying, controller, nameof(OnActorAdded));
    }

    private IEnumerator RemoveActors(Chat chat)
    {
        if (chat?.Actors == null)
            yield break;

        var retainedActors = new HashSet<ActorController>();
        foreach (var actorContext in chat.Actors)
        {
            if (actorContext?.Reference == null)
                continue;

            var actorController = actors.Get(actorContext.Reference);
            if (actorController != null)
                retainedActors.Add(actorController);
        }

        var outgoing = new List<ActorController>();
        foreach (var actor in actors)
        {
            if (actor != null && !retainedActors.Contains(actor))
                outgoing.Add(actor);
        }

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
        SafeInvoke(OnActorRemoved, NowPlaying, controller, nameof(OnActorRemoved));
    }

    public bool SetCurrentContext(ChatManagerContext context)
    {
        if (context == null)
            return false;
        var previousContextKey = CurrentContext?.Key;
        if (contexts.TryGetValue(context.Key, out var staleContext) && staleContext != null)
            if (context != staleContext)
                staleContext.MarkForDeath();
        contexts[context.Key] = context;
        CurrentContext = context;
        InvalidatePlaybackGeneration();
        DontDestroyOnLoad(context.gameObject);
        if (!string.IsNullOrWhiteSpace(previousContextKey) && !string.Equals(previousContextKey, context.Key, StringComparison.OrdinalIgnoreCase))
            RuntimeAssetCache.ReleaseOwner(RuntimeAssetCache.BuildContextOwnerKey(previousContextKey));
        OperatorTelemetry.CaptureMemorySnapshot("context_changed");
        SafeInvoke(OnContextChanged, context, nameof(OnContextChanged));
        return true;
    }

    public void UnregisterContext(ChatManagerContext context)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.Key))
            return;

        if (contexts.TryGetValue(context.Key, out var existing) && existing == context)
            contexts.Remove(context.Key);

        if (CurrentContext == context)
            CurrentContext = null;
    }

    public void SwitchCurrentContextAndScene(ChatManagerContext context, Action callback = null)
    {
        StartCoroutine(SetCurrentContextAndChangeScene(context, callback));
    }

    public IEnumerator SetCurrentContextAndChangeScene(ChatManagerContext context, Action callback = null)
    {
        var contextChanged = context.Key != CurrentContext?.Key;
        if (SetCurrentContext(context) && contextChanged)
            yield return ResetAndChangeScene(context.Key, context, callback);
    }

    private IEnumerator ResetAndChangeScene(string expectedKey, ChatManagerContext previousContext, Action callback = null)
    {
        ReadyForAction = false;

        var resetAsync = SceneManager.LoadSceneAsync(ResetScenePath);
        yield return resetAsync;

        yield return ChangeScene(expectedKey, previousContext, callback);
    }

    private IEnumerator ChangeScene(string expectedKey, ChatManagerContext previousContext, Action callback = null)
    {
        var async = SceneManager.LoadSceneAsync(CurrentContext.ScenePath);
        yield return async;

        yield return new WaitUntil(() => ContextReady(expectedKey, previousContext));

        SafeInvoke(callback, nameof(callback));
        ReadyForAction = true;
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
        OperatorTelemetry.CaptureMemorySnapshot("scene_loaded");
    }

    private bool StopPlaying(Chat chat)
    {
        return chat.ManagerContext == null || chat.ManagerContext.Key != CurrentContext?.Key;
    }

    private void ReleaseActorVoiceClips()
    {
        foreach (var actor in actors)
        {
            if (actor?.Voice == null)
                continue;

            actor.Voice.Stop();
            actor.Voice.clip = null;
        }
    }

    private bool ContextReady(string expectedKey, ChatManagerContext previousContext)
    {
        if (CurrentContext == null || CurrentContext.Key != expectedKey)
            return false;
        if (CurrentContext == previousContext)
            return false;

        var hasReadySpawnPointManager = GetReadySpawnPointManagers(CurrentContext).Length > 0;
        var hasFallbackSpawnPoint = GetValidFallbackSpawnPoints(CurrentContext).Length > 0;

        if (!hasReadySpawnPointManager && !hasFallbackSpawnPoint)
        {
            Debug.LogError($"Context '{CurrentContext.Name}' is missing both ready SpawnPointManagers and fallback spawn points.");
            return false;
        }

        return true;
    }

    private IEnumerator RunEventCoroutines(Func<Chat, IEnumerator> handlers, Chat chat)
    {
        yield return RunEventCoroutines(handlers, chat, playbackGeneration, chat?.ManagerContext?.Key ?? chat?.Key, nameof(handlers));
    }

    private IEnumerator RunCoroutineSafely(IEnumerator routine, string routineName, Func<bool> shouldAbort = null)
    {
        if (routine == null)
            yield break;

        bool complete = false;
        while (!complete)
        {
            if (shouldAbort?.Invoke() == true)
                yield break;

            object current = null;
            try
            {
                complete = !routine.MoveNext();
                if (!complete)
                    current = routine.Current;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ChatManager.{routineName} failed: {e}");
                yield break;
            }

            if (!complete)
                yield return current;
        }
    }

    private IEnumerator RunEventCoroutines(Func<Chat, IEnumerator> handlers, Chat chat, int generation, string expectedKey, string eventName)
    {
        if (handlers == null)
            yield break;

        foreach (Func<Chat, IEnumerator> handler in handlers.GetInvocationList())
        {
            if (!IsPlaybackCurrent(chat, expectedKey, generation))
                yield break;

            IEnumerator routine = null;
            try
            {
                routine = handler?.Invoke(chat);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ChatManager.{eventName} handler '{handler?.Method?.DeclaringType?.Name}.{handler?.Method?.Name}' failed before yielding: {e}");
            }

            if (routine == null)
                continue;

            bool complete = false;
            while (!complete)
            {
                if (!IsPlaybackCurrent(chat, expectedKey, generation))
                    yield break;

                object current = null;
                try
                {
                    complete = !routine.MoveNext();
                    if (!complete)
                        current = routine.Current;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"ChatManager.{eventName} handler '{handler?.Method?.DeclaringType?.Name}.{handler?.Method?.Name}' failed during execution: {e}");
                    break;
                }

                if (!complete)
                    yield return current;
            }
        }
    }

    private bool IsPlaybackCurrent(Chat chat, string expectedKey, int generation)
    {
        if (generation != playbackGeneration)
            return false;
        if (chat == null || string.IsNullOrEmpty(expectedKey))
            return false;
        if (chat.ManagerContext == null || chat.ManagerContext.Key != expectedKey)
            return false;
        return CurrentContext != null && CurrentContext.Key == expectedKey;
    }

    private void InvalidatePlaybackGeneration()
    {
        unchecked
        {
            playbackGeneration++;
        }
    }

    private ChatManagerContext ResolveLiveContext(Chat chat, string expectedKey)
    {
        var key = expectedKey ?? chat?.ManagerContext?.Key ?? chat?.Key;
        if (string.IsNullOrEmpty(key))
            return null;

        if (CurrentContext != null && CurrentContext.Key == key)
            return CurrentContext;

        if (contexts.TryGetValue(key, out var context) && context != null)
            return context;

        if (chat?.ManagerContext != null && chat.ManagerContext.Key == key)
            return chat.ManagerContext;

        return null;
    }

    private SpawnPointManager[] GetReadySpawnPointManagers(ChatManagerContext context)
    {
        var activeSpawnPoints = context?.ActiveSpawnPoints ?? Array.Empty<SpawnPointManager>();
        var readySpawnPoints = new List<SpawnPointManager>(activeSpawnPoints.Length);
        for (var i = 0; i < activeSpawnPoints.Length; i++)
        {
            var spawnPoint = activeSpawnPoints[i];
            if (spawnPoint != null && spawnPoint.IsReady)
                readySpawnPoints.Add(spawnPoint);
        }

        return readySpawnPoints.ToArray();
    }

    private Transform[] GetValidFallbackSpawnPoints(ChatManagerContext context)
    {
        var fallbackSpawnPoints = context?.ActiveFallbackSpawnPoints ?? Array.Empty<Transform>();
        var validFallbacks = new List<Transform>(fallbackSpawnPoints.Length);
        for (var i = 0; i < fallbackSpawnPoints.Length; i++)
        {
            var fallback = fallbackSpawnPoints[i];
            if (fallback != null && fallback.gameObject != null && fallback.gameObject.scene.IsValid() && fallback.gameObject.scene.isLoaded)
                validFallbacks.Add(fallback);
        }

        return validFallbacks.ToArray();
    }

    private static Transform GetFirstAvailableFallbackSpawnPoint(Transform[] fallbackSpawnPoints)
    {
        if (fallbackSpawnPoints == null)
            return null;

        for (var i = 0; i < fallbackSpawnPoints.Length; i++)
        {
            var fallback = fallbackSpawnPoints[i];
            if (fallback != null && fallback.childCount == 0)
                return fallback;
        }

        return null;
    }

    private IEnumerator RecoverLiveContext(Chat chat, string expectedKey, int generation, float timeoutSeconds = 1f)
    {
        var startedAt = Time.time;

        while (Time.time - startedAt < timeoutSeconds)
        {
            if (!IsPlaybackCurrent(chat, expectedKey, generation) && ResolveLiveContext(chat, expectedKey) == null)
                yield break;

            var liveContext = ResolveLiveContext(chat, expectedKey);
            if (liveContext != null)
            {
                chat.ManagerContext = liveContext;

                var hasReadySpawnPointManager = GetReadySpawnPointManagers(liveContext).Length > 0;
                var hasFallbackSpawnPoint = GetValidFallbackSpawnPoints(liveContext).Length > 0;
                if (hasReadySpawnPointManager || hasFallbackSpawnPoint)
                    yield break;
            }

            yield return null;
        }

        var recoveredContext = ResolveLiveContext(chat, expectedKey);
        if (recoveredContext != null)
            chat.ManagerContext = recoveredContext;
    }

    private void SafeInvoke(Action handlers, string eventName)
    {
        if (handlers == null)
            return;

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ChatManager.{eventName} handler '{handler?.Method?.DeclaringType?.Name}.{handler?.Method?.Name}' failed: {e}");
            }
        }
    }

    private void SafeInvoke<T>(Action<T> handlers, T arg, string eventName)
    {
        if (handlers == null)
            return;

        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler?.Invoke(arg);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ChatManager.{eventName} handler '{handler?.Method?.DeclaringType?.Name}.{handler?.Method?.Name}' failed: {e}");
            }
        }
    }

    private void SafeInvoke<T1, T2>(Action<T1, T2> handlers, T1 arg1, T2 arg2, string eventName)
    {
        if (handlers == null)
            return;

        foreach (Action<T1, T2> handler in handlers.GetInvocationList())
        {
            try
            {
                handler?.Invoke(arg1, arg2);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ChatManager.{eventName} handler '{handler?.Method?.DeclaringType?.Name}.{handler?.Method?.Name}' failed: {e}");
            }
        }
    }

    private void PostChatActorMemories(Chat chat)
    {
        if (StopPlaying(chat))
            return;
        if (chat?.Actors == null || chat.ManagerContext == null || CurrentContext == null)
            return;

        foreach (var actor in chat.Actors)
        {
            if (ChatManagerContext.Current.Key != chat.ManagerContext.Key)
                continue;
            if (actor == null || string.IsNullOrEmpty(actor.Memory) || actor.Reference == null)
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
        if (chat == null || StopPlaying(chat) || string.IsNullOrEmpty(chat.Title))
            return;

        var message = new DiscordWebhookMessage(
            "# :clapper: Now Streaming!", null, null,
            new DiscordEmbed
            {
                Title = chat.Title,
                Description = chat.Synopsis
            });

        if (!chat.NewEpisode)
        {
            DiscordManager.PutInQueue("#stream", message, posted =>
            {
                RecordNowPlayingMessage(chat, posted);
                FolderSource.RecordDiscordMessage(chat.Key, chat.FileName, posted);
                DiscordBotService.Instance?.AddDefaultReplayReactions(posted);
            });
            return;
        }

        DiscordManager.PutInQueue("#stream", message, posted => RecordNowPlayingMessage(chat, posted));
    }

    private void RecordNowPlayingMessage(Chat chat, DiscordPostedMessage message)
    {
        if (chat == null || message == null || StopPlaying(chat))
            return;

        nowPlayingDiscordMessage = message;
        DiscordBotService.Instance?.PinNowPlayingMessage(message);
    }

    private void UnpinNowPlayingMessage(Chat chat)
    {
        if (chat == null || nowPlayingDiscordMessage == null)
            return;

        DiscordBotService.Instance?.UnpinNowPlayingMessage(nowPlayingDiscordMessage);
        nowPlayingDiscordMessage = null;
    }
}
