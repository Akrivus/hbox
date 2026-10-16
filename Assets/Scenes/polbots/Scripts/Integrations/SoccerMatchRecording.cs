using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FStudio.MatchEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Responses;
using UnityEngine;

public partial class SoccerGameSource
{
    private const string SavedSetupKey = "soccer.matchSetup";
    private SoccerSavedMatch savedMatch;
    private SoccerMatchSetup pendingSetup;
    private RecordedPlaybackTimeline replayClock;
    private string replayOf;
    private double recordingStartedAt;
    private ChatManager recordingManager;
    private double nextRecordingSave;
    private double replayDuration;
    private readonly List<(RecordedPlaybackNode entry, ChatNode node)> recordedNodes = new List<(RecordedPlaybackNode, ChatNode)>();
    private bool IsSavedReplay => replayClock != null;
    public string SavedMatchDirectory => Path.Combine(Application.persistentDataPath, "soccer", boundContext.Key, "matches");
    private SoccerMatchStore MatchStore => new SoccerMatchStore(SavedMatchDirectory);

    [SerializeField] private string savedMatchToReplay;
    [SerializeField] private bool replaySavedDialogue = true;

    [ContextMenu("Replay saved soccer match")]
    private void ReplaySelectedMatch() => QueueSavedMatch(savedMatchToReplay, replaySavedDialogue);

    public SoccerSavedMatch LoadSavedMatch(string id) => MatchStore.Load(id);

    // Queue through ordinary scene/actor preparation; never mutate the original saved match.
    public void QueueSavedMatch(string id, bool replayDialogue = true)
    {
        if (!OwnsCurrentContext || isLoadingGame || isStartingGame || isGameLoaded || isUnloadingGame)
            throw new InvalidOperationException("Select the soccer channel and finish the active match first.");
        var source = MatchStore.Load(id);
        if (source.Setup.ChannelKey != boundContext.Key)
            throw new InvalidOperationException("Saved match belongs to another channel.");
        QueueMatchSetup(source.Setup, source, replayDialogue);
    }

    public void QueueMatchSetup(SoccerMatchSetup setup) => QueueMatchSetup(setup, null);

    private void QueueMatchSetup(SoccerMatchSetup setup, SoccerSavedMatch source, bool replayDialogue = true)
    {
        setup.Validate();
        if (isGameLoaded || isLoadingGame || isStartingGame || isUnloadingGame)
            throw new InvalidOperationException("Finish the active match before queuing another setup.");
        if (!OwnsCurrentContext || setup.ChannelKey != boundContext.Key)
            throw new InvalidOperationException("Match setup requires its active host channel.");
        var names = new[] { setup.HomeActor, setup.AwayActor }
            .Concat(source?.Nodes.Where(n => n.Kind == "node").Select(n => (string)n.Node["Actor"]) ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
        var cast = names.Select(name => ResolveActor(name) ?? throw new InvalidOperationException($"Missing replay actor '{name}'.")).ToArray();
        var chat = new Chat(new Idea($"Mode: Soccer\nHome: {setup.HomeActor}\nAway: {setup.AwayActor}") { Source = "soccer-saved-match" }, boundContext)
        {
            Title = $"{setup.HomeActor} vs {setup.AwayActor}" + (source == null || !replayDialogue ? " (saved setup)" : " (seeded replay)"),
            Topic = "Mode: Soccer", Actors = cast.Select(actor => new ActorContext(actor)).ToArray()
        };
        chat.GeneratedData[SavedSetupKey] = JsonConvert.SerializeObject(setup);
        if (source != null) chat.GeneratedData["soccer.replayOf"] = source.Id;
        if (source != null && replayDialogue)
        {
            chat.GeneratedData["soccer.replayOf"] = source.Id;
            chat.GeneratedData["soccer.replayDuration"] = source.Duration.ToString(CultureInfo.InvariantCulture);
            chat.RecordedMatchPlayback = new RecordedPlaybackTimeline { Nodes = source.Nodes.OrderBy(n => n.Offset).ThenBy(n => n.Sequence).ToList() };
        }
        else
        {
            // A silent node keeps the ordinary chat path valid; the match itself owns its lifetime.
            chat.Nodes.Add(new ChatNode(cast[0], ""));
        }
        chat.Lock();
        ChatManager.Instance.AddToPlayList(chat);
    }

    private int ReadMatchSeed(Chat trigger)
    {
        var text = FindMetadata(trigger?.Topic, "MatchSeed");
        if (string.IsNullOrEmpty(text))
            text = FindMetadata(trigger?.Idea?.Prompt, "MatchSeed");
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            return seed;
        if (!string.IsNullOrWhiteSpace(text))
            throw new FormatException("MatchSeed must be a signed 32-bit integer.");
        return config.MatchSeed ?? BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
    }

    private bool PrepareMatchRecording(Chat trigger, int seed)
    {
        try
        {
            FinishMatchRecording();
            pendingSetup = null;
            replayClock = trigger?.RecordedMatchPlayback;
            replayOf = null;
            if (trigger?.GeneratedData != null && trigger.GeneratedData.TryGetValue(SavedSetupKey, out var json))
            {
                pendingSetup = JsonConvert.DeserializeObject<SoccerMatchSetup>(json);
                pendingSetup.Validate();
                if (pendingSetup.ChannelKey != boundContext.Key || pendingSetup.HomeActor != homeActor.Name || pendingSetup.AwayActor != awayActor.Name)
                    throw new FormatException("Saved setup participants/channel mismatch.");
                if (pendingSetup.UnityVersion != Application.unityVersion || pendingSetup.FixedDeltaTime != Time.fixedDeltaTime || pendingSetup.TimeScale != Time.timeScale ||
                    pendingSetup.EngineSettingsJson != JsonUtility.ToJson(EngineSettings.Current))
                    throw new FormatException("Engine settings, Unity version, or clock settings changed since this match was saved.");

                var restoredHome = SoccerSetupSnapshots.Restore(pendingSetup.Home, teams);
                FStudio.Database.TeamEntry restoredAway;

                try
                {
                    restoredAway = SoccerSetupSnapshots.Restore(pendingSetup.Away, teams);
                }
                catch
                {
                    SoccerSetupSnapshots.Destroy(restoredHome);
                    throw;
                }

                SoccerSetupSnapshots.Destroy(homeTeam);
                SoccerSetupSnapshots.Destroy(awayTeam);
                homeTeam = restoredHome;
                awayTeam = restoredAway;
                rosterContext = pendingSetup.RosterContext;
                trigger.GeneratedData.TryGetValue("soccer.replayOf", out replayOf);
                if (trigger.GeneratedData.TryGetValue("soccer.replayDuration", out var duration))
                    replayDuration = double.Parse(duration, CultureInfo.InvariantCulture);
            }
            else
            {
                if (MatchManager.SoccerSeedPatchVersion != 1)
                    throw new InvalidOperationException("Apply the FootballSimulator seed patch.");
                var defaults = new MatchCreateRequest(homeTeam, awayTeam);
                pendingSetup = new SoccerMatchSetup
                {
                    Seed = seed, ChannelKey = boundContext.Key, HomeActor = homeActor.Name, AwayActor = awayActor.Name,
                    Home = SoccerSetupSnapshots.Capture(homeTeam), Away = SoccerSetupSnapshots.Capture(awayTeam),
                    RosterContext = rosterContext, AiLevel = (int)defaults.aiLevel, DayTime = (int)defaults.dayTime,
                    MatchTimeLimit = config.MatchTimeLimit, EngineSettingsJson = JsonUtility.ToJson(EngineSettings.Current),
                    UnityVersion = Application.unityVersion, FixedDeltaTime = Time.fixedDeltaTime, TimeScale = Time.timeScale
                };
                // Constructor clones player objects; these temporary defaults are never loaded into the engine.
                foreach (var team in new[] { defaults.homeTeam, defaults.awayTeam })
                {
                    foreach (var player in team.Players)
                        Destroy(player);
                    Destroy(team);
                }
            }
            pendingSetup.Validate();
            return true;
        }
        catch (Exception error)
        {
            if (replayClock != null)
                replayClock.Finished = true;
            replayClock = null;
            SoccerSetupSnapshots.Destroy(homeTeam);
            SoccerSetupSnapshots.Destroy(awayTeam);
            homeTeam = awayTeam = null;
            SetStartupStatus($"Saved match rejected: {error.Message}");
            return false;
        }
    }

    private void BeginMatchRecording()
    {
        savedMatch = new SoccerSavedMatch { Id = currentMatchId, ReplayOf = replayOf, Setup = pendingSetup };
        recordingStartedAt = Time.timeAsDouble;
        nextRecordingSave = recordingStartedAt + 5;
        recordedNodes.Clear();
        recordingManager = ChatManager.Instance;
        recordingManager.OnChatNodeActivated += CaptureActivatedNode;
        recordingManager.OnChatNodeInterrupted += CaptureInterruptedNode;
        if (announcerService != null)
            announcerService.OnLinePlayed += CaptureAnnouncer;
        if (replayClock != null)
        {
            replayClock.DispatchCue = entry =>
            {
                if (entry.Kind == "announcer") PlaySavedAnnouncer(entry);
                if (entry.Kind == "interrupt") recordingManager?.InterruptActiveNode((string)entry.Node["Notes"]);
            };
            replayClock.StartedAt = recordingStartedAt;
        }
        SaveMatchRecording();
    }

    private void CaptureActivatedNode(ChatNode node)
    {
        if (savedMatch == null || !OwnsCurrentContext || recordingManager.NowPlaying?.Key != boundContext.Key) return;
        var entry = new RecordedPlaybackNode
        {
            Sequence = savedMatch.Nodes.Count, Offset = Time.timeAsDouble - recordingStartedAt,
            MatchMinute = MatchManager.Current != null ? MatchManager.Current.minutes : 0,
            SourceChat = recordingManager.NowPlaying?.FileName, Node = JObject.FromObject(node)
        };
        savedMatch.Nodes.Add(entry);
        recordedNodes.Add((entry, node));
    }

    private void CaptureAnnouncer(string text, AudioClip clip)
    {
        if (savedMatch == null || IsSavedReplay) return;
        savedMatch.Nodes.Add(new RecordedPlaybackNode
        {
            Sequence = savedMatch.Nodes.Count, Kind = "announcer", Offset = Time.timeAsDouble - recordingStartedAt,
            Node = JObject.FromObject(new { Say = text, AudioData = clip.ToBase64(), Frequency = clip.frequency })
        });
    }

    private void CaptureInterruptedNode(string notes)
    {
        if (savedMatch == null || !OwnsCurrentContext) return;
        savedMatch.Nodes.Add(new RecordedPlaybackNode
        {
            Sequence = savedMatch.Nodes.Count, Kind = "interrupt", Offset = Time.timeAsDouble - recordingStartedAt,
            Node = JObject.FromObject(new { Notes = notes })
        });
    }

    public void PlaySavedAnnouncer(RecordedPlaybackNode entry)
    {
        if (!IsSavedReplay || announcerAudio == null) return;
        var node = entry.Node.ToObject<ChatNode>();
        var clip = node.AudioClip;
        if (clip == null) return;
        announcerAudio.clip = clip;
        announcerAudio.Play();
        Destroy(clip, clip.length + 1);
    }

    private void SaveMatchRecording()
    {
        if (savedMatch == null) return;
        try
        {
            // Audio may hydrate after activation. Refresh audio only, preserving the original node/action snapshot.
            foreach (var pair in recordedNodes)
            {
                pair.entry.Node["AudioData"] = pair.node.AudioData;
                pair.entry.Node["Frequency"] = pair.node.Frequency;
            }
            savedMatch.Duration = Time.timeAsDouble - recordingStartedAt;
            MatchStore.Save(savedMatch);
        }
        catch (Exception error)
        {
            Debug.LogError($"Match recording save failed: {error}");
        }
    }

    private void TickMatchRecording()
    {
        if (IsSavedReplay && savedMatch != null && !isUnloadingGame && !isLoadingGame && !isStartingGame &&
            (replayClock.Finished || recordingManager?.NowPlaying?.RecordedMatchPlayback != replayClock || ChatManager.SkipToEnd ||
             Time.timeAsDouble - recordingStartedAt > replayDuration + 1))
        {
            StartCoroutine(UnloadGame().AsCoroutine());
            return;
        }
        if (savedMatch != null && Time.timeAsDouble >= nextRecordingSave)
        {
            nextRecordingSave = Time.timeAsDouble + 5;
            SaveMatchRecording();
        }
    }

    private void CompleteRecordedResult()
    {
        if (savedMatch == null) return;
        savedMatch.Status = "completed";
        savedMatch.HomeScore = MatchManager.Current.homeTeamScore;
        savedMatch.AwayScore = MatchManager.Current.awayTeamScore;
        SaveMatchRecording();
    }

    private void FinishMatchRecording()
    {
        if (savedMatch != null)
        {
            if (savedMatch.Status != "completed")
                savedMatch.Status = "aborted";
            SaveMatchRecording();
            savedMatch = null;
        }
        if (recordingManager != null)
            recordingManager.OnChatNodeActivated -= CaptureActivatedNode;
        if (recordingManager != null)
            recordingManager.OnChatNodeInterrupted -= CaptureInterruptedNode;
        recordingManager = null;

        if (announcerService != null)
            announcerService.OnLinePlayed -= CaptureAnnouncer;
        recordedNodes.Clear();
        
        if (replayClock != null)
            replayClock.Finished = true;
        replayClock = null;
    }
}
