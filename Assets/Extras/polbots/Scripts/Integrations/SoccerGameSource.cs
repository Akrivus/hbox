using FStudio.Database;
using FStudio.Events;
using FStudio.MatchEngine;
using FStudio.MatchEngine.Events;
using FStudio.UI.MatchThemes.MatchEvents;
using Shared.Responses;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SoccerGameSource : MonoBehaviour, IConfigurable<SoccerConfigs>
{
    public static SoccerGameSource Instance;

    private const string GameScene = "3rdParty/FootballSimulator/_StartingScene";

    public event System.Action OnMatchStart;
    public event System.Action OnMatchEnd;
    public event System.Action<string> OnEmit;

    private List<string> addedScenes = new List<string>();
    private Dictionary<string, string[]> lines = new Dictionary<string, string[]>();

    [SerializeField]
    private ChatGenerator generator;

    [SerializeField]
    private ShareScreenUIManager shareScreenUiManager;

    [SerializeField]
    private TextAsset prompt1;

    [SerializeField]
    private TextAsset prompt2;

    [SerializeField]
    private TeamEntry[] teams;

    [SerializeField]
    private AudioSource teamAudio;

    [SerializeField, Range(0f, 1f)]
    private float maxVolume;

    [SerializeField]
    private float fadeOutDuration;

    private Actor homeActor;
    private Actor awayActor;
    private TeamEntry homeTeam;
    private TeamEntry awayTeam;
    private float volume;

    private bool isSceneLoaded;
    private bool isGameLoaded;

    private float lastGameTime;
    private string gameEventLog;

    private SoccerConfigs config;

    public void Configure(SoccerConfigs config)
    {
        maxVolume = config.MaxVolume;
        lines = config.Lines;
        this.config = config;

        MatchManager.MatchTimeLimit = config.MatchTimeLimit;

        ChatManager.Instance.AfterIntermission += TriggerGame;

        if (config.GameOnStart)
            ChatManager.Instance.OnChatQueueEmpty += BreakTheSilence;

        if (config.GameOnBatchEnd)
            RedditSource.Instance.OnBatchEnd += BreakTheSilence;

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    public void BreakTheSilence()
    {
        if (isGameLoaded && !string.IsNullOrEmpty(gameEventLog))
            Push();
        else
        {
            var time = Time.time - lastGameTime;
            if (time < config.TimeBetweenGames)
                return;
            var text = string.Format(prompt1.text, gameEventLog);
            var idea = new Idea(text);
            generator.AddIdeaToQueue(idea);
        }
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
        Instance = this;
        ConfigManager.Instance.RegisterConfig(typeof(SoccerConfigs), "soccer", config => Configure((SoccerConfigs) config));
        RegisterEmissionEvents();
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        volume = Mathf.Lerp(volume, 0, Time.deltaTime / fadeOutDuration);
        teamAudio.volume = isGameLoaded ? Mathf.Clamp01(volume) * maxVolume : 0;
    }

    private void TriggerGame(Chat chat)
    {
        if (isGameLoaded || chat == null || config.TimeBetweenGames < Time.time - lastGameTime) return;

        var homeName = chat.Topic.Find("Home");
        var awayName = chat.Topic.Find("Away");

        if (config.RequireTextPatternMatch && (string.IsNullOrEmpty(homeName) || string.IsNullOrEmpty(awayName)))
            return;

        homeActor = ChatManager.Instance.Actors[homeName] ?? chat.Actors[0].Reference;
        awayActor = ChatManager.Instance.Actors[awayName] ?? chat.Actors[1].Reference;

        if (homeActor != null && awayActor != null)
            StartCoroutine(LoadGame());
    }

    private IEnumerator LoadGame()
    {
        if (isGameLoaded)
            yield break;

        homeActor ??= ChatManager.Instance.NowPlaying.Actors[0].Reference;
        awayActor ??= ChatManager.Instance.NowPlaying.Actors[1].Reference;

        homeTeam = teams.Random();
        awayTeam = teams.Except(new[] { homeTeam }).Random();

        RenameTeam(homeTeam, homeActor);
        RenameTeam(awayTeam, awayActor);

        lastGameTime = Time.time;
        gameEventLog = string.Empty;

        ChatManager.Instance.RemoveActorsOnCompletion = false;
        MatchManager.MatchShouldContinue = true;

        if (addedScenes.Count > 0)
            yield return UnloadGameScenes();
        if (!isSceneLoaded)
            yield return SceneManager.LoadSceneAsync(GameScene, LoadSceneMode.Additive);
        else
            yield return StartGame();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        addedScenes.Add(scene.name);
        if (!GameScene.Contains(scene.name))
            return;
        isSceneLoaded = true;

        if (!isGameLoaded)
            StartCoroutine(StartGame());
    }

    private IEnumerator StartGame()
    {
        if (isGameLoaded || MatchEngineLoader.Current == null)
            yield break;
        var match = new MatchCreateRequest(homeTeam, awayTeam);
        yield return MatchEngineLoader.CreateMatch(match).AsCoroutine();
        yield return MatchEngineLoader.Current.StartMatchEngine(new UpcomingMatchEvent(match), false, true).AsCoroutine();

        shareScreenUiManager.ShareScreenOn();
        isGameLoaded = true;
        OnMatchStart?.Invoke();
    }

    private IEnumerator CloseGame()
    {
        MatchManager.MatchShouldContinue = false;
        yield return new WaitForSeconds(10);
        yield return UnloadGame().AsCoroutine();
    }

    private async Task UnloadGame()
    {
        if (!isGameLoaded || MatchEngineLoader.Current == null)
            return;
        await MatchEngineLoader.Current.UnloadMatch();
        
        if (config.ClearSceneOnGameEnd)
            await UnloadGameScenes();
        isGameLoaded = false;
    }

    private async Task UnloadGameScenes()
    {
        var queue = new Queue<string>(addedScenes);
        while (queue.TryDequeue(out var scene))
            if (SceneManager.GetSceneByName(scene).isLoaded)
                await SceneManager.UnloadSceneAsync(scene);
        await Resources.UnloadUnusedAssets();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        addedScenes.Remove(scene.name);
        if (!GameScene.Contains(scene.name))
            return;
        isSceneLoaded = false;

        shareScreenUiManager.ShareScreenOff();
        OnMatchEnd?.Invoke();
    }

    private void RegisterEmissionEvents()
    {
        EventManager.Subscribe<FinalWhistleEvent>(e => StartCoroutine(CloseGame()));
        EventManager.Subscribe<GoalScoredEvent>(e => EmitToChat(e));
        RegisterMessageEvents();
    }

    private void RegisterMessageEvents()
    {
        EventManager.Subscribe<RefereeLongWhistleEvent>((e) => Emit(e));
        EventManager.Subscribe<RefereeShortWhistleEvent>((e) => Emit(e));
        EventManager.Subscribe<RefereeLastWhistleEvent>((e) => Emit(e));
        EventManager.Subscribe<FirstWhistleEvent>((e) => Emit(e));
        EventManager.Subscribe<FinalWhistleEvent>((e) => Emit(e));
        EventManager.Subscribe<BallHitTheWoodWorkEvent>((e) => Emit(e));
        EventManager.Subscribe<KickOffEvent>((e) => Emit(e));
        EventManager.Subscribe<OutEvent>((e) => Emit(e));
        EventManager.Subscribe<ShootWentOutEvent>((e) => Emit(e));
        EventManager.Subscribe<KeeperHitTheBallButCouldNotControlEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<KeeperSavesTheBallEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerDisbalancedEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerSlideTackleEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerTackledEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerPassEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerControlBallEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerWinTheBallEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerLossTheBallEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerChipShootEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerShootEvent>((e) => Emit(e, GetName(e)));
        EventManager.Subscribe<PlayerThrowInEvent>((e) => Emit(e, GetName(e)));
    }

    private void EmitToChat(GoalScoredEvent e)
    {
        Emit($"# :soccer: GOAL! {Score} ({e.Scorer.Name}, {Minutes})");
        volume += 1.0f;
        volume *= Mathf.Clamp01(volume) * maxVolume;
    }

    private void Emit(IBaseEvent e, string name = null, bool push = false) {
        var key = e.GetType().Name;
        if (!lines.ContainsKey(key))
            return;
        var line = lines[key].Random();
        var log = string.Format(line,
            name,
            Score,
            Minutes);
        Emit(log, push);
    }

    private void Emit(string log, bool push = true)
    {
        gameEventLog += log + "\n";
        volume += 0.1f;
        if (push) Push();
        OnEmit?.Invoke(log);
    }

    private void Push()
    {
        var text = string.Format(prompt2.text, gameEventLog);
        var idea = new Idea(text);
        generator.AddIdeaToQueue(idea);
        gameEventLog = string.Empty;
    }

    private void RenameTeam(TeamEntry team, Actor actor)
    {
        team.Players.Zip(actor.Players, (p, n) => p.Name = n).ToList();
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
    public string Minutes => $"**{Mathf.CeilToInt(MatchManager.Current.minutes)}’**";
}
