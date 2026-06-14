using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FStudio;
using FStudio.Database;
using FStudio.Events;
using FStudio.Graphics.Cameras;
using FStudio.Loaders;
using FStudio.MatchEngine;
using FStudio.MatchEngine.Cameras;
using FStudio.MatchEngine.Events;
using FStudio.UI;
using FStudio.UI.Events;
using FStudio.UI.GamepadInput;
using FStudio.UI.MatchThemes;
using FStudio.UI.MatchThemes.MatchEvents;
using Shared.Responses;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class SoccerGameSource : MonoBehaviour, IConfigurable<SoccerConfigs>
{
    public static SoccerGameSource Instance;
    public bool IsSceneLoaded => isSceneLoaded;
    public bool IsGameLoaded => isGameLoaded;
    public int AddedSceneCount => addedScenes.Count;
    public string CurrentMatchId => currentMatchId;
    public SoccerAnnouncerService AnnouncerDiagnostics => announcerService;

    private const string GameScene = "3rdParty/FootballSimulator/_StartingScene";
    private const string SportsDiscordChannel = "#sports";

    public event Action OnMatchStart;
    public event Action OnMatchEnd;
    public event Action<string> OnEmit;
    public event Action OnBroadcastSignalLost;
    public event Action OnBroadcastSignalRestored;

    private readonly Dictionary<int, Scene> addedScenes = new Dictionary<int, Scene>();
    private readonly List<(Camera camera, string tag)> hostCameraTags = new List<(Camera camera, string tag)>();
    private readonly List<(string channelKey, DiscordPostedMessage message)> soccerDiscordMessages = new List<(string channelKey, DiscordPostedMessage message)>();
    private Dictionary<string, string[]> lines = new Dictionary<string, string[]>();
    private int soccerDiscordGeneration;

    [SerializeField]
    private ChatGenerator generator;

    [SerializeField]
    private TeamEntry[] teams;

    [SerializeField]
    private AudioSource teamAudio;

    [SerializeField]
    private AudioSource announcerAudio;

    [SerializeField, Range(0f, 1f)]
    private float maxVolume;

    [SerializeField]
    private float fadeOutDuration;

    [SerializeField]
    private RenderTexture matchBroadcastTexture;

    [SerializeField]
    private bool resizeBroadcastTextureToMediaScreen = true;

    [SerializeField]
    private float broadcastWatchdogInterval = 0.25f;

    [SerializeField]
    private float duplicateSystemSuppressInterval = 2f;

    [SerializeField]
    private bool waitForInterruptPrewarm = true;

    [SerializeField]
    private float interruptPrewarmTimeoutSeconds = 45f;

    private Actor homeActor;
    private Actor awayActor;
    private TeamEntry homeTeam;
    private TeamEntry awayTeam;
    private float volume;

    private bool isSceneLoaded;
    private bool isGameLoaded;
    private bool isLoadingGame;
    private bool isStartingGame;
    private bool isUnloadingGame;
    private bool isConfigured;
    private bool isTearingDown;
    private bool isBroadcastSignalHealthy = true;

    private string currentMatchId;
    private string startedMatchId;
    private float lastGameTime;
    private float lastBroadcastWatchdogTime;
    private float lastDuplicateSystemSuppressTime;
    private string gameEventLog;
    private RenderTexture runtimeMatchBroadcastTexture;

    private SoccerConfigs config;
    private SoccerInterruptService interruptService;
    private SoccerIdeaService ideaService;
    private SoccerMatchStateService matchStateService;
    private SoccerAnnouncerService announcerService;
    private ChatManagerContext boundContext;
    private bool eventsBound;
    private bool configRegistered;
    private bool emissionEventsRegistered;
    private bool announcerEmitBound;
    private bool gameOnStartTriggered;
    private bool gameOnStartQueued;
    private bool preserveHostSceneLockHeld;
    private EventSystem hostEventSystem;

    public void Configure(SoccerConfigs config)
    {
        maxVolume = config.MaxVolume;
        lines = config.Lines;
        this.config = config;
        interruptService?.Configure(config);
        announcerService?.Configure(config, announcerAudio);

        UnbindContextEvents();

        boundContext = GetComponentInParent<ChatManagerContext>() ?? ChatManagerContext.Current;
        if (boundContext == null)
            return;

        boundContext.AfterIntermission += TriggerGame;

        if (config.GameOnBatchEnd)
            boundContext.OnChatQueueEmpty += BreakTheSilence;

        QueueGameOnStartIfNeeded();

        eventsBound = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        isConfigured = true;
    }

    public void BreakTheSilence()
    {
        if (isGameLoaded || string.IsNullOrWhiteSpace(gameEventLog))
            return;

        var time = Time.time - lastGameTime;
        if (time < config.TimeBetweenGames)
            return;

        PushIdea(gameEventLog);
        gameEventLog = string.Empty;
    }

    private IEnumerator StartConfiguredGame()
    {
        gameOnStartQueued = true;
        yield return null;
        yield return new WaitUntilTimer(() => IsReadyForGameOnStart() && !isLoadingGame && !isStartingGame && !isUnloadingGame, 30);

        if (config?.GameOnStart != true ||
            !IsReadyForGameOnStart() ||
            isGameLoaded ||
            isLoadingGame ||
            isStartingGame ||
            isUnloadingGame)
        {
            gameOnStartQueued = false;
            yield break;
        }

        if (!TrySelectDefaultActors())
        {
            Debug.LogWarning("SoccerGameSource.GameOnStart could not start because two soccer-capable actors were not available.");
            gameOnStartQueued = false;
            yield break;
        }

        gameOnStartTriggered = true;
        yield return LoadGame();
        yield return new WaitUntilTimer(() => isGameLoaded || (!isLoadingGame && !isStartingGame && !isUnloadingGame), 30);

        if (!isGameLoaded)
            gameOnStartTriggered = false;

        gameOnStartQueued = false;
    }

    private void QueueGameOnStartIfNeeded()
    {
        if (config?.GameOnStart != true || gameOnStartTriggered || gameOnStartQueued || isGameLoaded)
            return;

        StartCoroutine(StartConfiguredGame());
    }

    private bool IsReadyForGameOnStart()
    {
        var manager = ChatManager.Instance;
        if (manager == null || !manager.ReadyForAction)
            return false;

        return boundContext != null &&
            !boundContext.Dead &&
            boundContext.IsActive &&
            manager.CurrentContext == boundContext;
    }

    public void IncrementVolume()
    {
        maxVolume = Mathf.Clamp01(maxVolume + 0.1f);
    }

    public void DecrementVolume()
    {
        maxVolume = Mathf.Clamp01(maxVolume - 0.1f);
    }

    public void ToggleGame()
    {
        if (isGameLoaded)
            StartCoroutine(UnloadGame().AsCoroutine());
        else
            StartCoroutine(LoadGame());
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        Instance = this;
        matchStateService = new SoccerMatchStateService();
        interruptService = new SoccerInterruptService(generator);
        ideaService = new SoccerIdeaService(generator);
        announcerService = new SoccerAnnouncerService(generator != null ? generator.GetComponent<TextToSpeechGenerator>() : null);
    }

    private void Start()
    {
        TryRegisterRuntimeBindings();
    }

    private void OnDestroy()
    {
        EmergencyTeardown();
        StopAllCoroutines();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        UnbindContextEvents();

        if (announcerEmitBound)
        {
            OnEmit -= HandleAnnouncerEmit;
            announcerEmitBound = false;
        }

        isConfigured = false;

        if (Instance == this)
            Instance = null;
    }

    private void OnDisable()
    {
        if (Instance == this)
            EmergencyTeardown();
    }

    private void Update()
    {
        TryRegisterRuntimeBindings();
        volume = Mathf.Lerp(volume, 0, Time.deltaTime / fadeOutDuration);
        teamAudio.volume = isGameLoaded ? Mathf.Clamp01(volume) * maxVolume : 0;
        interruptService?.Tick();
        announcerService?.Tick(interruptService?.HasPendingInterrupt() ?? false);
        TickBroadcastWatchdog();
        TickDuplicateSceneSystemSuppressor();
        QueueGameOnStartIfNeeded();
    }

    private void TryRegisterRuntimeBindings()
    {
        if (!configRegistered)
        {
            var context = GetComponentInParent<ChatManagerContext>() ?? ChatManagerContext.Current;
            if (context?.ConfigManager != null)
            {
                boundContext = context;
                context.ConfigManager.RegisterConfig(typeof(SoccerConfigs), "soccer", cfg => Configure((SoccerConfigs)cfg));
                configRegistered = true;
            }
        }

        if (!emissionEventsRegistered)
        {
            RegisterEmissionEvents();
            emissionEventsRegistered = true;
        }

        if (!announcerEmitBound)
        {
            OnEmit += HandleAnnouncerEmit;
            announcerEmitBound = true;
        }

        QueueGameOnStartIfNeeded();
    }

    private void TriggerGame(Chat chat)
    {
        if (isGameLoaded || isLoadingGame || isStartingGame || isUnloadingGame || chat == null)
            return;

        if (!IsSoccerMode(chat))
            return;

        var homeName = FindMetadata(chat.Topic, "Home");
        if (string.IsNullOrWhiteSpace(homeName))
            homeName = FindMetadata(chat.Idea?.Prompt, "Home");

        var awayName = FindMetadata(chat.Topic, "Away");
        if (string.IsNullOrWhiteSpace(awayName))
            awayName = FindMetadata(chat.Idea?.Prompt, "Away");

        if (config.RequireTextPatternMatch && (string.IsNullOrEmpty(homeName) || string.IsNullOrEmpty(awayName)))
            return;

        homeActor = ResolveActor(homeName) ?? GetChatActor(chat, 0);
        awayActor = ResolveActor(awayName) ?? GetChatActor(chat, 1);

        if (homeActor != null && awayActor != null)
            StartCoroutine(LoadGame());
    }

    private Actor ResolveActor(string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName) || boundContext?.ActorsSearch == null)
            return null;

        return boundContext.ActorsSearch[actorName.Trim()];
    }

    private static Actor GetChatActor(Chat chat, int index)
    {
        if (chat?.Actors == null || index < 0 || index >= chat.Actors.Length)
            return null;

        return chat.Actors[index]?.Reference;
    }

    private bool TrySelectDefaultActors()
    {
        if (homeActor != null && awayActor != null)
            return true;

        var candidates = boundContext?.ActorsSearch?.List?
            .Where(actor => actor != null && actor.Players != null && actor.Players.Length > 0)
            .ToList();

        if (candidates == null || candidates.Count < 2)
            return false;

        if (homeActor == null)
            homeActor = candidates.Sample();

        if (awayActor == null || awayActor == homeActor)
            awayActor = candidates.Where(actor => actor != homeActor).ToArray().Sample();

        return homeActor != null && awayActor != null && homeActor != awayActor;
    }

    private static bool IsSoccerMode(Chat chat)
    {
        return IsSoccerMode(chat?.Topic) || IsSoccerMode(chat?.Idea?.Prompt);
    }

    private static bool IsSoccerMode(string text)
    {
        var mode = FindMetadata(text, "Mode");
        return string.Equals(mode, "Soccer", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindMetadata(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimStart('#', '_', '*').TrimStart();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line.Substring(key.Length).TrimStart();
            if (value.Length == 0 || value[0] != ':')
                continue;

            return value.Substring(1).Trim();
        }

        return string.Empty;
    }

    private void UnbindContextEvents()
    {
        if (!eventsBound || boundContext == null)
            return;

        boundContext.AfterIntermission -= TriggerGame;
        boundContext.OnChatQueueEmpty -= BreakTheSilence;
        eventsBound = false;
        boundContext = null;
    }

    private IEnumerator LoadGame()
    {
        if (isGameLoaded || isLoadingGame || isStartingGame || isUnloadingGame)
            yield break;

        isLoadingGame = true;

        PreserveHostScene();

        homeActor ??= GetChatActor(ChatManager.Instance?.NowPlaying, 0);
        awayActor ??= GetChatActor(ChatManager.Instance?.NowPlaying, 1);
        if ((homeActor == null || awayActor == null) && !TrySelectDefaultActors())
        {
            Debug.LogWarning("SoccerGameSource.LoadGame skipped because home/away actors could not be resolved.");
            ReleaseHostScene();
            isLoadingGame = false;
            yield break;
        }

        homeTeam = teams.Sample();
        awayTeam = teams.Except(new[] { homeTeam }).Sample();

        RenameTeam(homeTeam, homeActor);
        RenameTeam(awayTeam, awayActor);

        currentMatchId = Guid.NewGuid().ToString("N");
        startedMatchId = null;
        lastGameTime = Time.time;
        gameEventLog = string.Empty;
        DeleteSoccerDiscordMessages();
        matchStateService.BeginMatch(currentMatchId, homeActor, awayActor);
        OperatorTelemetry.CaptureMemorySnapshot("soccer_load_started");

        try
        {
            if (addedScenes.Count > 0)
                yield return UnloadGameScenes();
            if (!isSceneLoaded)
                yield return SceneManager.LoadSceneAsync(GameScene, LoadSceneMode.Additive);
            else
                yield return StartGame();
        }
        finally
        {
            isLoadingGame = false;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        addedScenes[scene.handle] = scene;
        if (!GameScene.Contains(scene.name))
            return;

        DisableFootballBoot(scene);
        DisableEmbeddedSceneUiConflicts(scene);
        isSceneLoaded = true;

        if (!isGameLoaded && !isStartingGame && !isUnloadingGame)
            StartCoroutine(StartGame());
    }

    private IEnumerator StartGame()
    {
        if (isGameLoaded || isStartingGame || isUnloadingGame)
            yield break;

        yield return new WaitUntilTimer(() => MatchEngineLoader.Current != null, 5);
        if (MatchEngineLoader.Current == null)
        {
            Debug.LogWarning("SoccerGameSource.StartGame skipped because MatchEngineLoader was not available after loading the soccer scene.");
            yield break;
        }

        if (startedMatchId == currentMatchId && !string.IsNullOrWhiteSpace(currentMatchId))
            yield break;

        isStartingGame = true;
        startedMatchId = currentMatchId;

        try
        {
            var match = new MatchCreateRequest(homeTeam, awayTeam);
            interruptService.BeginMatch(matchStateService, homeActor, awayActor);
            announcerService?.BeginMatch();
            StartCoroutine(InjectPregameWhenReady(currentMatchId));

            yield return MatchEngineLoader.CreateMatch(match).AsCoroutine();
            SuppressUpcomingMatchUi();
            PreserveHostScene();
            yield return MatchEngineLoader.Current.StartMatchEngine(new UpcomingMatchEvent(match), false, true).AsCoroutine();

            yield return new WaitForSeconds(1);

            matchStateService.MarkLive();

            VideoCallUIManager.Instance.ShareScreenOn();
            ConfigureBroadcastCamera();
            isGameLoaded = true;
            isBroadcastSignalHealthy = true;
            lastBroadcastWatchdogTime = Time.unscaledTime;
            OperatorTelemetry.CaptureMemorySnapshot("soccer_started");
            OnMatchStart?.Invoke();

            EventManager.Trigger(new CloseAllPanelsEvent());
        }
        finally
        {
            isStartingGame = false;
        }
    }

    private IEnumerator InjectPregameWhenReady(string matchId)
    {
        if (string.IsNullOrWhiteSpace(matchId) || interruptService == null)
            yield break;

        if (waitForInterruptPrewarm)
            yield return interruptService.Prewarm(Mathf.CeilToInt(interruptPrewarmTimeoutSeconds * 1000f)).AsCoroutine();

        if (isUnloadingGame || currentMatchId != matchId)
            yield break;

        yield return interruptService.TryInjectPregame().AsCoroutine();
    }

    private void SuppressUpcomingMatchUi()
    {
        SnapManager.Disable();

        foreach (var panel in Object.FindObjectsByType<UpcomingMatchPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (panel == null)
                continue;

            panel.enabled = false;

            if (panel.gameObject.activeSelf)
                panel.gameObject.SetActive(false);
        }
    }

    private IEnumerator CloseGame()
    {
        yield return new WaitForSeconds(10);
        yield return UnloadGame().AsCoroutine();
    }

    private async Task UnloadGame()
    {
        if (isUnloadingGame || (!isGameLoaded && !isSceneLoaded && addedScenes.Count == 0))
            return;

        isUnloadingGame = true;

        try
        {
            interruptService.EndMatch();
            announcerService?.EndMatch();
            if (MatchEngineLoader.Current != null)
                await MatchEngineLoader.Current.UnloadMatch();
            ClearBroadcastCamera();
            RestoreHostMainCameras();
            VideoCallUIManager.Instance?.ShareScreenOff();

            if (config.ClearSceneOnGameEnd)
                await UnloadGameScenes();
            FStudio.Utilities.DontDestroy.DestroyTracked();
            isGameLoaded = false;
        }
        finally
        {
            isUnloadingGame = false;
        }
    }

    private async Task UnloadGameScenes()
    {
        var queue = new Queue<int>(addedScenes.Keys);
        while (queue.TryDequeue(out var handle))
        {
            if (!addedScenes.TryGetValue(handle, out var scene))
                continue;

            if (scene.IsValid() && scene.isLoaded)
                await SceneManager.UnloadSceneAsync(scene);
        }

        addedScenes.Clear();
        await Resources.UnloadUnusedAssets();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        addedScenes.Remove(scene.handle);
        if (GameScene.Contains(scene.name))
            FinalizeSoccerTeardown("soccer_unloaded");
    }

    private void PreserveHostScene()
    {
        if (preserveHostSceneLockHeld)
        {
            SceneLoader.PreserveHostScene = true;
            return;
        }

        SceneLoader.PushPreserveHostScene();
        preserveHostSceneLockHeld = true;
    }

    private void ReleaseHostScene()
    {
        if (!preserveHostSceneLockHeld)
        {
            SceneLoader.PreserveHostScene = false;
            return;
        }

        SceneLoader.PopPreserveHostScene();
        preserveHostSceneLockHeld = false;
    }

    private void DisableFootballBoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            foreach (var boot in root.GetComponentsInChildren<Boot>(true))
            {
                boot.enabled = false;
            }

            foreach (var loader in root.GetComponentsInChildren<DefaultSceneLoader>(true))
            {
                loader.enabled = false;
            }
        }
    }

    private void DisableEmbeddedSceneUiConflicts(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            foreach (var eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                DisableEventSystem(eventSystem);

            foreach (var inputModule in root.GetComponentsInChildren<BaseInputModule>(true))
                inputModule.enabled = false;
        }
    }

    private void SuppressDuplicateSceneSystems()
    {
        if (!isSceneLoaded && !isLoadingGame && !isStartingGame && !isGameLoaded)
            return;

        var systems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (systems == null || systems.Length <= 1)
            return;

        if (hostEventSystem == null || !hostEventSystem || !hostEventSystem.enabled)
        {
            hostEventSystem = systems.FirstOrDefault(system =>
                system != null && !addedScenes.ContainsKey(system.gameObject.scene.handle)) ??
                EventSystem.current ??
                systems.FirstOrDefault(system => system != null);
        }

        foreach (var system in systems)
        {
            if (system == null || system == hostEventSystem)
                continue;

            DisableEventSystem(system);
        }

        if (hostEventSystem != null && hostEventSystem.enabled)
            EventSystem.current = hostEventSystem;
    }

    private void TickDuplicateSceneSystemSuppressor()
    {
        if (!isSceneLoaded && !isLoadingGame && !isStartingGame && !isGameLoaded)
            return;

        var interval = Mathf.Max(0.25f, duplicateSystemSuppressInterval);
        if (Time.unscaledTime - lastDuplicateSystemSuppressTime < interval)
            return;

        lastDuplicateSystemSuppressTime = Time.unscaledTime;
        SuppressDuplicateSceneSystems();
    }

    private static void DisableEventSystem(EventSystem eventSystem)
    {
        if (eventSystem == null)
            return;

        foreach (var inputModule in eventSystem.GetComponents<BaseInputModule>())
            inputModule.enabled = false;

        eventSystem.enabled = false;
        eventSystem.gameObject.SetActive(false);
    }

    private void ClaimFootballMainCamera()
    {
        RestoreHostMainCameras();

        var footballCamera = MainCamera.Current?.Camera;
        if (footballCamera == null)
            return;

        foreach (var camera in Camera.allCameras)
        {
            if (camera == null || camera.gameObject == null || camera == footballCamera)
                continue;

            if (camera.gameObject.CompareTag("MainCamera"))
            {
                hostCameraTags.Add((camera, camera.gameObject.tag));
                camera.gameObject.tag = "Untagged";
            }
        }

        footballCamera.gameObject.tag = "MainCamera";
    }

    private void RestoreHostMainCameras()
    {
        foreach (var (camera, tag) in hostCameraTags)
        {
            if (camera != null && camera.gameObject != null)
                camera.gameObject.tag = tag;
        }

        hostCameraTags.Clear();
    }

    private void ConfigureBroadcastCamera()
    {
        var broadcastTexture = GetBroadcastTexture();
        if (broadcastTexture == null)
            return;

        var footballCamera = MainCamera.Current?.Camera;
        if (footballCamera == null)
            return;

        footballCamera.targetTexture = broadcastTexture;
        VideoCallUIManager.Instance?.ShareScreenUIManager?.SetMediaTexture(broadcastTexture);
        DisableFootballAudioListener(footballCamera);
        if (footballCamera.gameObject.CompareTag("MainCamera"))
            footballCamera.gameObject.tag = "Untagged";
    }

    private void ClearBroadcastCamera()
    {
        var footballCamera = MainCamera.Current?.Camera;
        var broadcastTexture = GetActiveBroadcastTexture();
        if (footballCamera != null && footballCamera.targetTexture == broadcastTexture)
            footballCamera.targetTexture = null;

        VideoCallUIManager.Instance?.ShareScreenUIManager?.SetMediaTexture(matchBroadcastTexture);
        ReleaseRuntimeBroadcastTexture();
    }

    private void TickBroadcastWatchdog()
    {
        var broadcastTexture = GetBroadcastTexture();
        if (!isGameLoaded || broadcastTexture == null)
            return;

        if (Time.unscaledTime - lastBroadcastWatchdogTime < broadcastWatchdogInterval)
            return;

        lastBroadcastWatchdogTime = Time.unscaledTime;

        var footballCamera = MainCamera.Current?.Camera;
        if (footballCamera == null)
        {
            MarkBroadcastSignalLost();
            return;
        }

        if (footballCamera.targetTexture == broadcastTexture)
        {
            if (!isBroadcastSignalHealthy)
            {
                isBroadcastSignalHealthy = true;
                OnBroadcastSignalRestored?.Invoke();
            }

            return;
        }

        MarkBroadcastSignalLost();
        footballCamera.targetTexture = broadcastTexture;
        VideoCallUIManager.Instance?.ShareScreenUIManager?.SetMediaTexture(broadcastTexture);
        DisableFootballAudioListener(footballCamera);

        if (footballCamera.targetTexture == broadcastTexture)
        {
            isBroadcastSignalHealthy = true;
            OnBroadcastSignalRestored?.Invoke();
        }
    }

    private RenderTexture GetBroadcastTexture()
    {
        if (!resizeBroadcastTextureToMediaScreen)
            return matchBroadcastTexture;

        var mediaSize = VideoCallUIManager.Instance?.ShareScreenUIManager?.ActiveMediaScreenSize ?? Vector2.zero;
        var width = Mathf.Max(1, Mathf.RoundToInt(mediaSize.x));
        var height = Mathf.Max(1, Mathf.RoundToInt(mediaSize.y));
        if (width <= 1 || height <= 1)
            return matchBroadcastTexture;

        if (runtimeMatchBroadcastTexture != null &&
            runtimeMatchBroadcastTexture.width == width &&
            runtimeMatchBroadcastTexture.height == height)
        {
            return runtimeMatchBroadcastTexture;
        }

        ReleaseRuntimeBroadcastTexture();

        var descriptor = matchBroadcastTexture != null
            ? matchBroadcastTexture.descriptor
            : new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24);
        descriptor.width = width;
        descriptor.height = height;

        runtimeMatchBroadcastTexture = new RenderTexture(descriptor)
        {
            name = "Soccer Match Broadcast Runtime",
            filterMode = matchBroadcastTexture != null ? matchBroadcastTexture.filterMode : FilterMode.Bilinear,
            wrapMode = matchBroadcastTexture != null ? matchBroadcastTexture.wrapMode : TextureWrapMode.Clamp
        };
        runtimeMatchBroadcastTexture.Create();
        return runtimeMatchBroadcastTexture;
    }

    private RenderTexture GetActiveBroadcastTexture()
    {
        return runtimeMatchBroadcastTexture != null ? runtimeMatchBroadcastTexture : matchBroadcastTexture;
    }

    private void ReleaseRuntimeBroadcastTexture()
    {
        if (runtimeMatchBroadcastTexture == null)
            return;

        runtimeMatchBroadcastTexture.Release();
        Destroy(runtimeMatchBroadcastTexture);
        runtimeMatchBroadcastTexture = null;
    }

    private static void DisableFootballAudioListener(Camera footballCamera)
    {
        var listener = footballCamera != null ? footballCamera.GetComponent<AudioListener>() : null;
        if (listener != null)
            listener.enabled = false;
    }

    private void MarkBroadcastSignalLost()
    {
        if (!isBroadcastSignalHealthy)
            return;

        isBroadcastSignalHealthy = false;
        OnBroadcastSignalLost?.Invoke();
    }

    private void RegisterEmissionEvents()
    {
        EventManager.Subscribe<FinalWhistleEvent>(OnFinalWhistle);
        EventManager.Subscribe<GoalScoredEvent>(e => HandleInjectableEvent(e, BuildGoalLog(e), e.Scorer?.Name, true));
        EventManager.Subscribe<KeeperSavesTheBallEvent>(e => HandleInjectableEvent(e, null, GetName(e)));
        EventManager.Subscribe<BallHitTheWoodWorkEvent>(e => HandleInjectableEvent(e));
        EventManager.Subscribe<PlayerSlideTackleEvent>(e => HandleInjectableEvent(e, null, GetName(e)));
        EventManager.Subscribe<RefereeShortWhistleEvent>(e => HandleInjectableEvent(e));
        RegisterMessageEvents();
    }

    private async void OnFinalWhistle(FinalWhistleEvent e)
    {
        Emit(e);
        matchStateService.MarkPostgame();
        interruptService.EndMatch();
        announcerService?.EndMatch();

        var snapshot = BuildContinuitySnapshot();
        await PersistContinuitySnapshot("#soccer-live", snapshot);

        await ideaService.QueuePostgameIdeas(
            homeActor,
            awayActor,
            currentMatchId,
            Score,
            gameEventLog,
            matchStateService.GetRecentResidue());

        StartCoroutine(CloseGame());
    }

    private void RegisterMessageEvents()
    {
        EventManager.Subscribe<RefereeLongWhistleEvent>(e => Emit(e));
        EventManager.Subscribe<RefereeLastWhistleEvent>(e => Emit(e));
        EventManager.Subscribe<FirstWhistleEvent>(e => Emit(e));
        EventManager.Subscribe<KickOffEvent>(e => Emit(e));
        EventManager.Subscribe<OutEvent>(e => Emit(e));
        EventManager.Subscribe<ShootWentOutEvent>(e => Emit(e));
        EventManager.Subscribe<KeeperHitTheBallButCouldNotControlEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerDisbalancedEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerTackledEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerPassEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerControlBallEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerWinTheBallEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerLossTheBallEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerChipShootEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerShootEvent>(e => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerThrowInEvent>(e => Emit(e, GetName(e)));
    }

    private void HandleInjectableEvent(IBaseEvent e, string log = null, string primaryActor = null, bool highPriority = false)
    {
        if (log == null)
            log = BuildLog(e, primaryActor);
        if (string.IsNullOrWhiteSpace(log))
            return;

        Emit(log, false);
        _ = interruptService.TryInject(matchStateService.BuildSummary(e, log, primaryActor, highPriority));
    }

    private string BuildGoalLog(GoalScoredEvent e)
    {
        return $"# :soccer: GOAL! {Score} ({e.Scorer.Name}, {Minutes})";
    }

    private string BuildLog(IBaseEvent e, string name = null)
    {
        var key = e.GetType().Name;
        if (!lines.TryGetValue(key, out var variants))
            return null;

        return string.Format(variants.Sample(), name, Score, Minutes);
    }

    private void Emit(IBaseEvent e, string name = null)
    {
        var log = BuildLog(e, name);
        if (!string.IsNullOrWhiteSpace(log))
            Emit(log, false);
    }

    private void Emit(string log, bool push = true)
    {
        if (log.StartsWith("#") || log.StartsWith(":"))
            PostSoccerDiscordMessage(log);
        gameEventLog += log + "\n";
        matchStateService.AppendResidue(log);
        volume += 0.1f;
        if (push && !isGameLoaded)
            PushIdea(log);
        OnEmit?.Invoke(log);
    }

    private void PostSoccerDiscordMessage(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
            return;

        var generation = soccerDiscordGeneration;
        DiscordManager.PutInQueue(SportsDiscordChannel, new DiscordWebhookMessage(log, null, null), posted =>
        {
            if (posted == null)
                return;

            if (generation != soccerDiscordGeneration)
            {
                DiscordManager.DeleteWebhookMessageForContext(null, SportsDiscordChannel, posted.id);
                return;
            }

            soccerDiscordMessages.Add((SportsDiscordChannel, posted));
        });
    }

    private void DeleteSoccerDiscordMessages()
    {
        soccerDiscordGeneration++;

        if (soccerDiscordMessages.Count == 0)
            return;

        foreach (var entry in soccerDiscordMessages)
        {
            if (entry.message == null || string.IsNullOrWhiteSpace(entry.message.id))
                continue;

            DiscordManager.DeleteWebhookMessageForContext(null, entry.channelKey, entry.message.id);
        }

        soccerDiscordMessages.Clear();
    }

    private void HandleAnnouncerEmit(string line)
    {
        announcerService?.EnqueueLine(line);
    }

    private string BuildContinuitySnapshot()
    {
        return
            $"MatchId: {currentMatchId}\n" +
            $"Home: {homeActor?.Name}\n" +
            $"Away: {awayActor?.Name}\n" +
            $"Score: {Score}\n" +
            $"Recent live residue:\n- {string.Join("\n- ", matchStateService.GetRecentResidue())}";
    }

    private async Task PersistContinuitySnapshot(string bucketName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var resolver = new PromptResolver(generator.ManagerContext, "Soccer Mode", "Continuity", DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss"));
        await resolver.SaveOutput(text);

        var bucket = await MemoryBucket.Get(generator.ManagerContext, bucketName);
        await bucket.Add(resolver.Output);
        await bucket.Save();
    }

    private void PushIdea(string ideaText)
    {
        if (string.IsNullOrWhiteSpace(ideaText))
            return;

        generator.AddIdeaToQueue(new Idea(ideaText));
    }

    private void EmergencyTeardown()
    {
        if (isTearingDown)
            return;

        if (!isGameLoaded && !isSceneLoaded && addedScenes.Count == 0 && string.IsNullOrWhiteSpace(currentMatchId))
            return;

        isTearingDown = true;

        try
        {
            interruptService?.EndMatch();
            announcerService?.EndMatch();
            SafeClearSnapManager();
            FStudio.Utilities.DontDestroy.DestroyTracked();
            addedScenes.Clear();
            isLoadingGame = false;
            isStartingGame = false;
            isUnloadingGame = false;
            FinalizeSoccerTeardown("soccer_emergency_teardown");
        }
        finally
        {
            isTearingDown = false;
        }
    }

    private static void SafeClearSnapManager()
    {
        try
        {
            SnapManager.Clear();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SoccerGameSource teardown skipped SnapManager.Clear after vendor UI disposal: {ex.Message}");
        }
    }

    private void FinalizeSoccerTeardown(string reason)
    {
        var hadLiveMatchState = isGameLoaded || isSceneLoaded || !string.IsNullOrWhiteSpace(currentMatchId);

        ClearBroadcastCamera();
        RestoreHostMainCameras();

        if (VideoCallUIManager.Instance != null)
            VideoCallUIManager.Instance.ShareScreenOff();

        DeleteSoccerDiscordMessages();
        ReleaseHostScene();
        isSceneLoaded = false;
        isGameLoaded = false;
        gameOnStartTriggered = false;
        gameOnStartQueued = false;
        startedMatchId = null;
        currentMatchId = null;
        matchStateService.EndMatch();
        announcerService?.EndMatch();

        if (hadLiveMatchState)
        {
            OperatorTelemetry.CaptureMemorySnapshot(reason);
            OnMatchEnd?.Invoke();
        }
    }

    private void RenameTeam(TeamEntry team, Actor actor)
    {
        team.Players.Zip(actor.Players, (player, name) => player.Name = name).ToList();
        team.TeamLogo.TeamLogoColor1 = actor.Color1;
        team.TeamLogo.TeamLogoColor2 = actor.Color2;
        team.TeamName = actor.Name;

        team.AwayKit.Color1 = actor.Color2;
        team.AwayKit.Color2 = actor.Color1;
        team.AwayKit.GKColor1 = actor.Color3;
        team.AwayKit.GKColor2 = actor.Color3;

        team.HomeKit.Color1 = actor.Color1;
        team.HomeKit.Color2 = actor.Color2;
        team.HomeKit.GKColor1 = actor.Color3;
        team.HomeKit.GKColor2 = actor.Color3;
    }

    private string GetName(AbstractPlayerEvent e)
    {
        return $"{e.Player.MatchPlayer.Player.Name} ({e.Player.GameTeam.Team.Team.TeamName})";
    }

    public string Names => $"**{homeActor.Name}** {homeActor.Costume} and **{awayActor.Name}** {awayActor.Costume}";
    public string Score => $"**{homeActor.Name}** {homeActor.Costume} **{MatchManager.Current.homeTeamScore} - {MatchManager.Current.awayTeamScore}** {awayActor.Costume} **{awayActor.Name}**";
    public string Minutes => $"**{Mathf.CeilToInt(MatchManager.Current.minutes)}'**";
}
