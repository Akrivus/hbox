using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

public partial class SoccerGameSource : MonoBehaviour, IConfigurable<SoccerConfigs>
{
    public static SoccerGameSource Instance;
    public bool IsSceneLoaded => isSceneLoaded;
    public bool IsGameLoaded => isGameLoaded;
    public bool IsFullScreenMatch => OwnsCurrentContext && isGameLoaded && config?.FullScreenMatch == true;
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
    private string rosterContext = string.Empty;
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
    private Camera directCamera;
    private float directCameraDepth;
    private Rect directCameraRect;
    private RenderTexture directCameraTexture;
    private SoccerEventStore eventStore;
    private string scheduledEventId;
    private string completedMatchId;
    private bool scheduleError;
    private readonly List<string> matchHighlights = new List<string>();
    private readonly List<Action> emissionUnsubscribers = new List<Action>();
    [SerializeField] private string startupStatus = "Waiting for configuration";
    public string StartupStatus => startupStatus;
    private bool OwnsCurrentContext => Instance == this && boundContext != null && !boundContext.Dead &&
        boundContext.IsActive && ChatManager.Instance?.CurrentContext == boundContext;

    private void SetStartupStatus(string status)
    {
        if (startupStatus == status) return;
        startupStatus = status;
        Debug.Log($"[Soccer] {status}", this);
    }

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

        try
        {
            eventStore = new SoccerEventStore(Path.Combine(Application.persistentDataPath, "soccer", boundContext.Key));
            var schedules = config.Schedules ?? Array.Empty<SoccerScheduleConfig>();
            foreach (var schedule in schedules)
            {
                if (schedule == null || schedule.Recurrence == null)
                    throw new ArgumentException("Soccer schedules need an ID and recurrence pattern.");
                SoccerEventStore.ValidateId(schedule.Id);
                schedule.Recurrence.Validate();
            }
            if (schedules.GroupBy(schedule => schedule.Id).Any(group => group.Count() > 1))
                throw new ArgumentException("Soccer schedule IDs must be unique.");
            scheduleError = false;
        }
        catch (Exception error)
        {
            scheduleError = true;
            Debug.LogError($"Soccer schedule disabled: {error.Message}");
        }

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
        SetStartupStatus($"Configured for {boundContext.Key}; GameOnStart={config.GameOnStart}");
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
        matchHighlights.Clear();
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
            SetStartupStatus($"Auto-start waiting: ready={ChatManager.Instance?.ReadyForAction}, ownsContext={OwnsCurrentContext}, loading={isLoadingGame}, starting={isStartingGame}");
            gameOnStartQueued = false;
            yield break;
        }

        if (!TrySelectDefaultActors())
        {
            SetStartupStatus("Auto-start rejected: fewer than two current actors with assigned prefabs");
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
        if (boundContext == null || ChatManager.Instance?.CurrentContext != boundContext || boundContext.Dead) return;
        if (config?.GameOnStart != true || gameOnStartTriggered || gameOnStartQueued || isGameLoaded)
            return;

        StartCoroutine(StartConfiguredGame());
    }

    private bool IsReadyForGameOnStart()
    {
        var manager = ChatManager.Instance;
        if (manager == null || !manager.ReadyForAction)
            return false;

        return OwnsCurrentContext;
    }

    public void IncrementVolume()
    {
        maxVolume = Mathf.Clamp01(maxVolume + 0.1f);
    }

    public void DecrementVolume()
    {
        maxVolume = Mathf.Clamp01(maxVolume - 0.1f);
    }

    [ContextMenu("Toggle soccer match")]
    public void ToggleGame()
    {
        if (isGameLoaded)
            StartCoroutine(UnloadGame().AsCoroutine());
        else
            StartCoroutine(LoadGame());
    }

    private void Awake()
    {
        // Preloaded contexts persist; the current context claims ownership in Update.
        if (Instance == null) Instance = this;
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
        foreach (var unsubscribe in emissionUnsubscribers) unsubscribe();
        emissionUnsubscribers.Clear();

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
        if (boundContext == null || !boundContext.IsActive || boundContext.Dead ||
            ChatManager.Instance?.CurrentContext != boundContext) return;
        if (Instance != this)
        {
            if (Instance != null) Instance.EmergencyTeardown();
            Instance = this;
            SetStartupStatus("Current polbots context claimed soccer ownership");
        }
        volume = Mathf.Lerp(volume, 0, Time.deltaTime / fadeOutDuration);
        teamAudio.volume = isGameLoaded ? Mathf.Clamp01(volume) * maxVolume : 0;

        TickMatchRecording();

        interruptService?.Tick();
        announcerService?.Tick(interruptService?.HasPendingInterrupt() ?? false);

        TickBroadcastWatchdog();
        TickDuplicateSceneSystemSuppressor();
        QueueGameOnStartIfNeeded();
        TickScheduledMatches();
    }

    private void TickScheduledMatches()
    {
        if (scheduleError || eventStore == null || generator == null || config == null ||
            !IsReadyForGameOnStart() || ChatManager.IsPaused || generator.IsGenerating ||
            generator.QueueDepth > 0 || isGameLoaded || isLoadingGame || isStartingGame || isUnloadingGame)
            return;
        try
        {
            var match = eventStore.NextDue(GetScheduledFixtures(), DateTimeOffset.Now);
            if (match == null) return;
            var currentScene = ChatManager.Instance.NowPlaying;
            var prompt = match.BuildPrompt();
            if (match.SeriesId != null && currentScene?.Key == boundContext.Key)
            {
                var discussion = $"{currentScene.Title}\n{currentScene.Synopsis}\n{currentScene.Topic}";
                if (discussion.Length > 1500) discussion = discussion.Substring(0, 1500);
                prompt += "\nCurrent polbots discussion, separate from recorded match facts:\n" + discussion;
            }
            match.Validate();
            if (boundContext.ActorsSearch[match.Home] == null || boundContext.ActorsSearch[match.Away] == null)
                throw new InvalidOperationException($"Scheduled match '{match.Id}' references an unknown polbots actor.");
            // Persist before queueing so a reload cannot duplicate generation.
            eventStore.SetStatus(match.Id, "queued");
            generator.AddIdeaToQueue(new Idea(prompt, "scheduler", "soccer-schedule", match.Id));
        }
        catch (Exception error)
        {
            scheduleError = true;
            Debug.LogError($"Soccer schedule paused: {error.Message}");
        }
    }

    private IEnumerable<SoccerScheduledMatch> GetScheduledFixtures()
    {
        var eligible = boundContext.ActorsSearch.List.Where(actor => actor != null && actor.Prefab != null && !actor.IsLegacy)
            .Select(actor => actor.Name).ToArray();
        var now = DateTimeOffset.Now;
        return (config.Schedules ?? Array.Empty<SoccerScheduleConfig>())
            .Select(schedule => eventStore.GetRollingFixture(schedule, eligible, now))
            .Where(match => match != null).ToArray();
    }

    [ContextMenu("Cancel outstanding soccer schedule events")]
    private void ResetOutstandingScheduleEvents()
    {
        if (isGameLoaded || isLoadingGame || generator?.IsGenerating == true || generator?.QueueDepth > 0) return;
        foreach (var match in config == null ? Enumerable.Empty<SoccerScheduledMatch>() : GetScheduledFixtures())
            if (eventStore?.Status(match.Id) == "queued" || eventStore?.Status(match.Id) == "started")
                eventStore.SetStatus(match.Id, "cancelled");
        // A new ID is needed to retry; existing generated replays must not start it again.
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
        if (!OwnsCurrentContext || chat?.Key != boundContext.Key) return;
        if (isGameLoaded || isLoadingGame || isStartingGame || isUnloadingGame || chat == null)
            return;

        if (!IsSoccerMode(chat))
            return;
        SetStartupStatus("Matched Mode: Soccer; resolving participants");

        var eventId = chat.Idea?.Source == "soccer-schedule" ? chat.Idea.Slug : null;
        if (eventId != null && eventStore?.Status(eventId) != "queued") return;

        var homeName = FindMetadata(chat.Topic, "Home");
        if (string.IsNullOrWhiteSpace(homeName))
            homeName = FindMetadata(chat.Idea?.Prompt, "Home");

        var awayName = FindMetadata(chat.Topic, "Away");
        if (string.IsNullOrWhiteSpace(awayName))
            awayName = FindMetadata(chat.Idea?.Prompt, "Away");

        if (config.RequireTextPatternMatch && (string.IsNullOrEmpty(homeName) || string.IsNullOrEmpty(awayName)))
        {
            SetStartupStatus("Soccer rejected: RequireTextPatternMatch needs Home and Away metadata");
            return;
        }

        homeActor = ResolveActor(homeName) ?? GetChatActor(chat, 0);
        awayActor = ResolveActor(awayName) ?? GetChatActor(chat, 1);

        if (eventId != null)
        {
            var scheduled = GetScheduledFixtures().FirstOrDefault(match => match.Id == eventId);
            if (scheduled == null || homeActor != ResolveActor(scheduled.Home) || awayActor != ResolveActor(scheduled.Away))
            {
                Debug.LogError($"Scheduled match '{eventId}' did not preserve its configured participants; cancel it before retrying with a new ID.");
                return;
            }
        }

        if (homeActor != null && awayActor != null)
            StartCoroutine(LoadGame(chat));
        else
            SetStartupStatus("Soccer rejected: unable to resolve two actors from metadata or scene cast");
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
        if (homeActor != null && awayActor != null && homeActor != awayActor)
            return true;

        var candidates = boundContext?.ActorsSearch?.List?
            .Where(actor => actor != null && actor.Prefab != null && !actor.IsLegacy)
            .ToList();

        if (candidates == null || candidates.Count < 2)
            return false;

        if (homeActor == null)
            homeActor = candidates.Sample();

        if (awayActor == null || awayActor == homeActor)
            awayActor = candidates.Where(actor => actor != homeActor).ToArray().Sample();

        return homeActor != null && awayActor != null && homeActor != awayActor;
    }

    public static bool IsSoccerMode(Chat chat)
    {
        return SoccerModeMetadata.IsMatchRequest(chat?.Topic, chat?.Idea?.Prompt, chat?.Idea?.Source);
    }

    private static bool IsSoccerMode(string text)
    {
        var mode = FindMetadata(text, "Mode");
        return string.Equals(mode, "Soccer", StringComparison.OrdinalIgnoreCase);
    }

    public static string FindMetadata(string text, string key)
    {
        return SoccerModeMetadata.Read(text, key);
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

    private IEnumerator LoadGame(Chat trigger = null)
    {
        if (!OwnsCurrentContext)
        {
            SetStartupStatus("Match rejected: this Soccer component does not own the current context");
            yield break;
        }
        if (isGameLoaded || isLoadingGame || isStartingGame || isUnloadingGame)
            yield break;

        isLoadingGame = true;
        if (addedScenes.Count > 0)
            yield return UnloadGameScenes();
        FinishMatchRecording();
        SoccerSetupSnapshots.Destroy(homeTeam);
        SoccerSetupSnapshots.Destroy(awayTeam);
        homeTeam = awayTeam = null;

        var availableTeams = (teams ?? Array.Empty<TeamEntry>()).Where(team => team != null && team.Players?.Length > 0).Distinct().ToArray();
        if (availableTeams.Length < 2)
        {
            SetStartupStatus("Match rejected: assign at least two distinct simulator team assets with player slots");
            isLoadingGame = false;
            yield break;
        }

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

        trigger ??= ChatManager.Instance?.NowPlaying;
        int setupSeed;
        try
        {
            setupSeed = ReadMatchSeed(trigger);
        }
        catch (FormatException error)
        {
            SetStartupStatus(error.Message);
            if (trigger?.RecordedMatchPlayback != null) trigger.RecordedMatchPlayback.Finished = true;
            ReleaseHostScene();
            isLoadingGame = false;
            yield break;
        }

        var selection = new System.Random(setupSeed);
        var homeTemplate = availableTeams[selection.Next(availableTeams.Length)];
        var awayTemplates = availableTeams.Except(new[] { homeTemplate }).ToArray();

        homeTeam = SoccerSetupSnapshots.Clone(homeTemplate);
        awayTeam = SoccerSetupSnapshots.Clone(awayTemplates[selection.Next(awayTemplates.Length)]);

        trigger ??= ChatManager.Instance?.NowPlaying;
        var story = trigger?.Idea?.Prompt ?? string.Empty;
        var castNames = (boundContext?.ActorsSearch?.List ?? new List<Actor>())
            .Where(actor => actor != null).SelectMany(actor => (actor.Aliases ?? Array.Empty<string>()).Append(actor.Name)).ToArray();
        SoccerPreparedRoster prepared = null;
        try
        {
            prepared = IsSoccerMode(trigger) ? SoccerPreparedRoster.Read(trigger) : null;
            if (prepared != null)
            {
                if (prepared.Home != homeActor.Name || prepared.Away != awayActor.Name || prepared.Names?.Length != 2)
                    throw new FormatException("Prepared soccer roster does not match the selected teams.");
                SoccerRosterService.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    home = prepared.Names[0], away = prepared.Names[1]
                }), homeTeam.Players.Length, awayTeam.Players.Length, castNames);
            }
        }
        catch (Exception error)
        {
            Debug.LogError($"Cannot start match with invalid prepared roster: {error.Message}");
            ReleaseHostScene();
            isLoadingGame = false;
            yield break;
        }
        if (prepared == null)
        {
            Debug.LogWarning("No prepared soccer roster (legacy skit or manual match); using fixed actor names.");
            prepared = new SoccerPreparedRoster
            {
                Home = homeActor.Name, Away = awayActor.Name, Story = story,
                Names = new[]
                {
                    SoccerRosterService.Fallback(homeActor.Name, homeActor.Players, homeTeam.Players.Length),
                    SoccerRosterService.Fallback(awayActor.Name, awayActor.Players, awayTeam.Players.Length)
                }
            };
        }
        RenameTeam(homeTeam, homeActor, prepared.Names[0]);
        RenameTeam(awayTeam, awayActor, prepared.Names[1]);
        rosterContext = prepared.Context;
        if (!PrepareMatchRecording(trigger, setupSeed))
        {
            ReleaseHostScene();
            isLoadingGame = false;
            yield break;
        }
        currentMatchId = Guid.NewGuid().ToString("N");
        BeginMatchRecording();
        scheduledEventId = trigger?.Idea?.Source == "soccer-schedule" ? trigger.Idea.Slug : null;
        completedMatchId = null;
        if (scheduledEventId != null)
        {
            try { eventStore.SetStatus(scheduledEventId, "started"); }
            catch (Exception error)
            {
                Debug.LogError($"Cannot persist scheduled match start: {error}");
                FinishMatchRecording();
                ReleaseHostScene();
                isLoadingGame = false;
                yield break;
            }
        }
        startedMatchId = null;
        lastGameTime = Time.time;
        gameEventLog = string.Empty;
        matchHighlights.Clear();
        DeleteSoccerDiscordMessages();
        matchStateService.BeginMatch(currentMatchId, homeActor, awayActor, rosterContext);
        OperatorTelemetry.CaptureMemorySnapshot("soccer_load_started");

        try
        {
            if (!isSceneLoaded)
            {
                if (!Application.CanStreamedLevelBeLoaded(GameScene))
                {
                    SetStartupStatus($"Match rejected: scene '{GameScene}' is missing from enabled build scenes");
                    FinishMatchRecording();
                    ReleaseHostScene();
                    yield break;
                }
                SetStartupStatus($"Loading football scene for {homeActor.Name} vs {awayActor.Name}");
                yield return SceneManager.LoadSceneAsync(GameScene, LoadSceneMode.Additive);
            }
            else
                yield return StartGame();
        }
        finally
        {
            isLoadingGame = false;
            if (!isSceneLoaded && !isStartingGame && !isGameLoaded)
                FinishMatchRecording();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!OwnsCurrentContext || (!isLoadingGame && !isStartingGame && !isGameLoaded)) return;
        addedScenes[scene.handle] = scene;
        if (!GameScene.Contains(scene.name))
            return;

        DisableFootballBoot(scene);
        DisableEmbeddedSceneUiConflicts(scene);
        isSceneLoaded = true;
        SetStartupStatus("Football scene loaded; waiting for MatchEngineLoader");

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
            FinishMatchRecording();
            ReleaseHostScene();
            yield break;
        }

        if (startedMatchId == currentMatchId && !string.IsNullOrWhiteSpace(currentMatchId))
            yield break;

        isStartingGame = true;
        SetStartupStatus("Creating simulator match");
        startedMatchId = currentMatchId;

        try
        {
            SoccerSimulationRandom.Reset(savedMatch.Setup.Seed);
            var match = new MatchCreateRequest(homeTeam, awayTeam)
            {
                aiLevel = (FStudio.MatchEngine.Enums.AILevel)savedMatch.Setup.AiLevel,
                dayTime = (FStudio.Data.DayTimes)savedMatch.Setup.DayTime
            };
            if (!IsSavedReplay)
            {
                interruptService.BeginMatch(matchStateService, homeActor, awayActor);
                announcerService?.BeginMatch();
                StartCoroutine(InjectPregameWhenReady(currentMatchId));
            }

            yield return MatchEngineLoader.CreateMatch(match).AsCoroutine();
            SuppressUpcomingMatchUi();
            PreserveHostScene();
            yield return MatchEngineLoader.Current.StartMatchEngine(new UpcomingMatchEvent(match), savedMatch.Setup.HomeAwayKit, savedMatch.Setup.AwayAwayKit).AsCoroutine();

            yield return new WaitForSeconds(1);

            matchStateService.MarkLive();

            if (config.FullScreenMatch)
                VideoCallUIManager.Instance?.ShareScreenUIManager?.FullScreenMatchOn(config.FullScreenHeads);
            else
                VideoCallUIManager.Instance?.ShareScreenOn();
            ConfigureBroadcastCamera();
            isGameLoaded = true;

            savedMatch.Status = "live";
            SaveMatchRecording();
            SetStartupStatus("Match live");
            isBroadcastSignalHealthy = true;
            lastBroadcastWatchdogTime = Time.unscaledTime;
            OperatorTelemetry.CaptureMemorySnapshot("soccer_started");
            OnMatchStart?.Invoke();

            EventManager.Trigger(new CloseAllPanelsEvent());
        }
        finally
        {
            isStartingGame = false;
            if (!isGameLoaded)
            {
                FinishMatchRecording();
                ReleaseHostScene();
            }
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
        var closingMatchId = currentMatchId;
        yield return new WaitForSeconds(10);
        while (currentMatchId == closingMatchId && IsSavedReplay && Time.timeAsDouble - recordingStartedAt < replayDuration)
            yield return null;
        if (currentMatchId != closingMatchId) yield break;
        yield return UnloadGame().AsCoroutine();
    }

    private async Task UnloadGame()
    {
        if (isUnloadingGame || (!isGameLoaded && !isSceneLoaded && addedScenes.Count == 0))
            return;

        isUnloadingGame = true;

        try
        {
            FinishMatchRecording();
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
        if (!OwnsCurrentContext) return;
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
        if (config?.FullScreenMatch == true)
        {
            ConfigureDirectCamera();
            return;
        }
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
        RestoreDirectCamera();
        var footballCamera = MainCamera.Current?.Camera;
        var broadcastTexture = GetActiveBroadcastTexture();
        if (footballCamera != null && footballCamera.targetTexture == broadcastTexture)
            footballCamera.targetTexture = null;

        VideoCallUIManager.Instance?.ShareScreenUIManager?.SetMediaTexture(matchBroadcastTexture);
        ReleaseRuntimeBroadcastTexture();
    }

    private void TickBroadcastWatchdog()
    {
        if (config?.FullScreenMatch == true)
        {
            if (!isGameLoaded) return;
            if (Time.unscaledTime - lastBroadcastWatchdogTime < broadcastWatchdogInterval) return;
            lastBroadcastWatchdogTime = Time.unscaledTime;
            if (MainCamera.Current?.Camera == null) { MarkBroadcastSignalLost(); return; }
            ConfigureDirectCamera();
            if (!isBroadcastSignalHealthy)
            {
                isBroadcastSignalHealthy = true;
                OnBroadcastSignalRestored?.Invoke();
            }
            return;
        }
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

    private void ConfigureDirectCamera()
    {
        var camera = MainCamera.Current?.Camera;
        if (camera == null) return;
        if (directCamera != camera)
        {
            RestoreDirectCamera();
            directCamera = camera;
            directCameraDepth = camera.depth;
            directCameraRect = camera.rect;
            directCameraTexture = camera.targetTexture;
        }
        camera.targetTexture = null;
        camera.rect = new Rect(0, 0, 1, 1);
        // The existing polbots canvas is Screen Space Overlay; actor cameras still render their head textures.
        camera.depth = Camera.allCameras.Where(c => c != camera && c.targetTexture == null)
            .Select(c => c.depth).Append(directCameraDepth).Max() + 1;
        DisableFootballAudioListener(camera);
    }

    private void RestoreDirectCamera()
    {
        if (directCamera != null)
        {
            directCamera.depth = directCameraDepth;
            directCamera.rect = directCameraRect;
            directCamera.targetTexture = directCameraTexture;
        }
        directCamera = null;
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
        SubscribeOwned<FinalWhistleEvent>(OnFinalWhistle);
        SubscribeOwned<GoalScoredEvent>(e => HandleInjectableEvent(e, BuildGoalLog(e), e.Scorer?.Name, true));
        SubscribeOwned<KeeperSavesTheBallEvent>(e => HandleInjectableEvent(e, null, GetName(e)));
        SubscribeOwned<BallHitTheWoodWorkEvent>(e => HandleInjectableEvent(e));
        SubscribeOwned<PlayerSlideTackleEvent>(e => HandleInjectableEvent(e, null, GetName(e)));
        SubscribeOwned<RefereeShortWhistleEvent>(e => HandleInjectableEvent(e));
        RegisterMessageEvents();
    }

    private void SubscribeOwned<T>(Action<T> callback) where T : IBaseEvent
    {
        Action<T> handler = value =>
        {
            if (this != null && OwnsCurrentContext && (isGameLoaded || isStartingGame)) callback(value);
        };
        EventManager.Subscribe(handler);
        emissionUnsubscribers.Add(() => EventManager.UnSubscribe(handler));
    }

    private async void OnFinalWhistle(FinalWhistleEvent e)
    {
        if (!isGameLoaded || string.IsNullOrWhiteSpace(currentMatchId) || completedMatchId == currentMatchId) return;
        completedMatchId = currentMatchId;
        Emit(e);
        matchStateService.MarkPostgame();
        interruptService.EndMatch();
        announcerService?.EndMatch();

        CompleteRecordedResult();
        if (IsSavedReplay)
        {
            StartCoroutine(CloseGame());
            return;
        }

        try
        {
            eventStore?.SaveReport(new SoccerMatchReport
            {
                MatchId = currentMatchId, ScheduledEventId = scheduledEventId, ChannelKey = boundContext?.Key,
                SeriesId = scheduledEventId == null ? null : GetScheduledFixtures().FirstOrDefault(match => match.Id == scheduledEventId)?.SeriesId,
                Home = homeActor?.Name, Away = awayActor?.Name,
                HomeScore = MatchManager.Current.homeTeamScore, AwayScore = MatchManager.Current.awayTeamScore,
                CompletedAt = DateTimeOffset.Now, RosterContext = rosterContext,
                KeyEvents = matchHighlights.ToArray(), SavedMatchId = savedMatch?.Id, Seed = savedMatch?.Setup.Seed
            });
        }
        catch (Exception error) { Debug.LogError($"Soccer match report could not be saved: {error}"); }

        var finishedId = currentMatchId;
        var snapshot = BuildContinuitySnapshot();
        var finalScore = Score;
        var finalLog = gameEventLog;
        var residue = matchStateService.GetRecentResidue();
        var finalRoster = rosterContext;
        var finalHome = homeActor;
        var finalAway = awayActor;
        try
        {
            await PersistContinuitySnapshot("#soccer-live", snapshot);
            if (this != null && currentMatchId == finishedId)
                await ideaService.QueuePostgameIdeas(finalHome, finalAway, finishedId, finalScore, finalLog, residue, finalRoster);
        }
        catch (Exception error) { Debug.LogError($"Soccer postgame generation failed: {error}"); }
        finally
        {
            if (this != null && isActiveAndEnabled && currentMatchId == finishedId)
                StartCoroutine(CloseGame());
        }
    }

    private void RegisterMessageEvents()
    {
        SubscribeOwned<RefereeLongWhistleEvent>(e => Emit(e));
        SubscribeOwned<RefereeLastWhistleEvent>(e => Emit(e));
        SubscribeOwned<FirstWhistleEvent>(e => Emit(e));
        SubscribeOwned<KickOffEvent>(e => Emit(e));
        SubscribeOwned<OutEvent>(e => Emit(e));
        SubscribeOwned<ShootWentOutEvent>(e => Emit(e));
        SubscribeOwned<KeeperHitTheBallButCouldNotControlEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerDisbalancedEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerTackledEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerPassEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerControlBallEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerWinTheBallEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerLossTheBallEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerChipShootEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerShootEvent>(e => Emit(e, GetName(e)));
        SubscribeOwned<PlayerThrowInEvent>(e => Emit(e, GetName(e)));
    }

    private void HandleInjectableEvent(IBaseEvent e, string log = null, string primaryActor = null, bool highPriority = false)
    {
        if (log == null)
            log = BuildLog(e, primaryActor);
        if (string.IsNullOrWhiteSpace(log))
            return;

        Emit(log, false);
        if (isGameLoaded && matchHighlights.Count < 100) matchHighlights.Add(log);
        if (!IsSavedReplay)
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
        if (IsSavedReplay) return;
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
            $"{rosterContext}\n" +
            $"Match highlights:\n- {string.Join("\n- ", matchHighlights)}\n" +
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
        FinishMatchRecording();
        SoccerSetupSnapshots.Destroy(homeTeam);
        SoccerSetupSnapshots.Destroy(awayTeam);

        homeTeam = awayTeam = null;

        var hadLiveMatchState = isGameLoaded || isSceneLoaded || !string.IsNullOrWhiteSpace(currentMatchId);

        ClearBroadcastCamera();
        RestoreHostMainCameras();

        if (VideoCallUIManager.Instance != null)
            VideoCallUIManager.Instance.ShareScreenOff();

        DeleteSoccerDiscordMessages();
        ReleaseHostScene();
        isSceneLoaded = false;
        isGameLoaded = false;
        gameOnStartQueued = false;
        startedMatchId = null;
        currentMatchId = null;
        scheduledEventId = null;
        matchStateService.EndMatch();
        announcerService?.EndMatch();

        if (hadLiveMatchState)
        {
            OperatorTelemetry.CaptureMemorySnapshot(reason);
            OnMatchEnd?.Invoke();
        }
    }

    private void RenameTeam(TeamEntry team, Actor actor, string[] names)
    {
        for (var i = 0; i < team.Players.Length; i++)
            team.Players[i].Name = names[i];
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
