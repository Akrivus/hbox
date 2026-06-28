using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FStudio.MatchEngine.Events;
using UnityEngine;

public sealed class SoccerInterruptService
{
    private readonly ChatGenerator generator;
    private readonly SentimentTagger sentimentTagger;
    private readonly TextToSpeechGenerator ttsGenerator;
    private readonly SoccerInterruptPolicy policy = new SoccerInterruptPolicy();

    private readonly Dictionary<SoccerPacketBank, Queue<SoccerInterruptPacket>> packetsByBank = new Dictionary<SoccerPacketBank, Queue<SoccerInterruptPacket>>();
    private readonly string[] seedEventTypes =
    {
        "Pregame",
        nameof(GoalScoredEvent),
        nameof(KeeperSavesTheBallEvent),
        nameof(BallHitTheWoodWorkEvent),
        nameof(PlayerSlideTackleEvent),
        nameof(RefereeShortWhistleEvent)
    };

    private readonly HashSet<string> replenishing = new HashSet<string>();
    private const string InterruptNodeMarker = "[soccer-interrupt]";
    private const int FirstPlayableNodeCount = 1;
    private readonly Dictionary<string, string[]> interruptSeeds = new Dictionary<string, string[]>();

    private string currentMatchId;
    private SoccerMatchStateService matchState;
    private Actor homeActor;
    private Actor awayActor;
    private int generationVersion;
    private long latestInjectedSequence;
    private SoccerInterruptPacket pendingPacket;
    private bool pendingInjectionInProgress;
    private bool liveGenerationInProgress;
    private SoccerEventSummary queuedGenerationSummary;

    public SoccerInterruptService(ChatGenerator generator)
    {
        this.generator = generator;
        sentimentTagger = generator != null ? generator.GetComponent<SentimentTagger>() : null;
        ttsGenerator = generator != null ? generator.GetComponent<TextToSpeechGenerator>() : null;
    }

    public void BeginMatch(SoccerMatchStateService matchState, Actor homeActor, Actor awayActor)
    {
        generationVersion++;
        this.matchState = matchState;
        currentMatchId = matchState?.CurrentMatchId;
        this.homeActor = homeActor;
        this.awayActor = awayActor;
        packetsByBank.Clear();
        replenishing.Clear();
        pendingPacket = null;
        pendingInjectionInProgress = false;
        liveGenerationInProgress = false;
        queuedGenerationSummary = null;
        latestInjectedSequence = 0;

        PrimeSeedBanks(generationVersion);
    }

    public void Configure(SoccerConfigs config)
    {
        interruptSeeds.Clear();

        if (config?.InterruptSeeds == null)
            return;

        foreach (var pair in config.InterruptSeeds)
            interruptSeeds[pair.Key] = pair.Value;
    }

    public async Task Prewarm(int timeoutMs, int goalPackets = 1, int whistlePackets = 1)
    {
        var generation = generationVersion;
        if (string.IsNullOrEmpty(currentMatchId) || generation != generationVersion)
            return;

        var tasks = new List<Task>
        {
            Replenish(SoccerPacketBank.Pregame, generation),
            Replenish(SoccerPacketBank.LiveGeneric, generation, nameof(RefereeShortWhistleEvent)),
            Replenish(SoccerPacketBank.LiveScoreSensitive, generation, nameof(GoalScoredEvent))
        };

        if (goalPackets > 1)
            tasks.Add(Replenish(SoccerPacketBank.LiveScoreSensitive, generation));
        if (whistlePackets > 1)
            tasks.Add(Replenish(SoccerPacketBank.LiveGeneric, generation));

        var prewarm = Task.WhenAll(tasks);
        if (timeoutMs <= 0)
        {
            await prewarm;
            return;
        }

        await Task.WhenAny(prewarm, Task.Delay(timeoutMs));
    }

    public void EndMatch()
    {
        generationVersion++;
        RemoveQueuedInterruptNodes();
        currentMatchId = null;
        matchState = null;
        homeActor = null;
        awayActor = null;
        packetsByBank.Clear();
        replenishing.Clear();
        pendingPacket = null;
        pendingInjectionInProgress = false;
        liveGenerationInProgress = false;
        queuedGenerationSummary = null;
        latestInjectedSequence = 0;
    }

    public void Tick()
    {
        if (pendingInjectionInProgress || pendingPacket == null || !CanInjectNow(pendingPacket))
            return;

        if (IsStale(pendingPacket, pendingPacket.SeedEvent))
        {
            PublishStatus($"Dropped stale queued soccer interrupt: {DescribeSummary(pendingPacket.SeedEvent)}", 4f);
            pendingPacket.Superseded = true;
            pendingPacket = null;
            return;
        }

        var packet = pendingPacket;
        pendingInjectionInProgress = true;
        _ = PrepareAndInjectPendingPacket(packet, generationVersion);
    }

    private async Task PrepareAndInjectPendingPacket(SoccerInterruptPacket packet, int generation)
    {
        try
        {
            if (packet == null || packet != pendingPacket || !IsCurrent(packet.SeedEvent, generation))
                return;

            if (!await PrepareFirstPacketAudio(packet, generation))
                return;

            if (packet != pendingPacket || IsStale(packet, packet.SeedEvent) || !CanInjectNow(packet))
                return;

            if (TryInjectPacket(packet))
            {
                PublishStatus($"Injected queued soccer interrupt: {DescribeSummary(packet.SeedEvent)}", 4f);
                packet.Consumed = true;
                latestInjectedSequence = Math.Max(latestInjectedSequence, packet.Sequence);
                pendingPacket = null;
                PrepareRemainingPacketAudio(packet, generation);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SoccerInterruptService pending injection failed: {e}");
        }
        finally
        {
            pendingInjectionInProgress = false;
        }
    }

    public bool HasPendingInterrupt()
    {
        if (liveGenerationInProgress || pendingInjectionInProgress || queuedGenerationSummary != null)
            return true;

        if (pendingPacket != null)
            return true;

        var chat = ChatManager.Instance?.NowPlaying;
        return chat != null && chat.Nodes.Any(node => node != null && node.New && node.Notes == InterruptNodeMarker);
    }

    public async Task<bool> TryInjectPregame()
    {
        var summary = BuildSeedSummary("Pregame", SoccerPacketBank.Pregame);
        var packet = Dequeue(SoccerPacketBank.Pregame, summary.EventType) ??
            await BuildPacket(summary, generationVersion, false);
        _ = Replenish(SoccerPacketBank.Pregame, generationVersion, summary.EventType);
        if (packet == null || packet.Nodes.Count == 0)
            return false;
        if (!CanInjectNow())
        {
            QueuePending(packet);
            return false;
        }

        if (!await PrepareFirstPacketAudio(packet, generationVersion))
            return false;

        var injected = TryInjectPacket(packet);
        if (injected)
        {
            packet.Consumed = true;
            PrepareRemainingPacketAudio(packet, generationVersion);
        }
        return injected;
    }

    public async Task<bool> TryInject(SoccerEventSummary summary)
    {
        var generation = generationVersion;
        if (!IsCurrent(summary, generation))
            return false;

        FlushQueuedPackets(summary);

        if (liveGenerationInProgress)
        {
            QueueGenerationSummary(summary);
            return false;
        }

        liveGenerationInProgress = true;
        try
        {
            return await TryInjectCore(summary, generation);
        }
        finally
        {
            liveGenerationInProgress = false;
            TryStartQueuedGeneration(generation);
        }
    }

    private async Task<bool> TryInjectCore(SoccerEventSummary summary, int generation)
    {
        var bank = policy.GetBank(summary);
        var packet = Dequeue(bank, summary.EventType);
        if (packet != null)
            _ = Replenish(bank, generation, summary.EventType);
        if (packet == null)
            packet = await BuildPacket(summary, generation, false);

        ApplySummary(packet, summary);

        if (packet == null || packet.Nodes.Count == 0)
            return false;

        if (IsStale(packet, summary))
        {
            PublishStatus($"Dropped stale soccer interrupt: {DescribeSummary(summary)}", 4f);
            return false;
        }

        if (!CanInjectNow(packet))
        {
            QueuePending(packet);
            PublishStatus($"Queued soccer interrupt: {DescribeSummary(summary)}", 4f);
            return false;
        }

        if (!await PrepareFirstPacketAudio(packet, generation))
            return false;

        if (IsStale(packet, summary))
        {
            PublishStatus($"Dropped stale soccer interrupt after audio prep: {DescribeSummary(summary)}", 4f);
            return false;
        }

        var injected = TryInjectPacket(packet);

        if (injected)
        {
            packet.Consumed = true;
            latestInjectedSequence = Math.Max(latestInjectedSequence, packet.Sequence);
            PrepareRemainingPacketAudio(packet, generation);
            PublishStatus($"Injected soccer interrupt: {DescribeSummary(summary)}", 4f);
        }
        else
        {
            PublishStatus($"Could not inject soccer interrupt: {DescribeSummary(summary)}", 4f);
        }

        return injected;
    }

    private SoccerInterruptPacket Dequeue(SoccerPacketBank bank, string eventType)
    {
        if (!packetsByBank.TryGetValue(bank, out var queue))
            return null;

        SoccerInterruptPacket match = null;
        var remaining = new Queue<SoccerInterruptPacket>();
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (next == null || next.Consumed || next.Superseded || next.ExpiresAtUtc <= DateTime.UtcNow)
                continue;

            var matches = string.Equals(next.TriggerEventType, eventType, StringComparison.Ordinal) || next.IsTemplatePacket;
            if (match == null && matches)
            {
                match = next;
                continue;
            }

            remaining.Enqueue(next);
        }

        while (remaining.Count > 0)
            queue.Enqueue(remaining.Dequeue());

        return match;
    }

    private void PrimeSeedBanks(int generation)
    {
        foreach (var eventType in seedEventTypes)
        {
            var bank = GetBankForSeed(eventType);
            _ = Replenish(bank, generation);
        }
    }

    private async Task Replenish(SoccerPacketBank bank, int generation, string eventType = null)
    {
        var replenishKey = BuildReplenishKey(bank, eventType);
        if (string.IsNullOrEmpty(currentMatchId) || replenishing.Contains(replenishKey) || generation != generationVersion)
            return;

        replenishing.Add(replenishKey);
        try
        {
            if (!packetsByBank.ContainsKey(bank))
                packetsByBank[bank] = new Queue<SoccerInterruptPacket>();

            var queue = packetsByBank[bank];
            var target = string.IsNullOrEmpty(eventType) ? GetBankTarget(bank) : policy.GetTargetCount(bank, eventType);
            PruneQueue(queue);
            var attemptsRemaining = Math.Max(target * 3, 3);

            while (CountReady(queue, eventType) < target &&
                attemptsRemaining-- > 0 &&
                !string.IsNullOrEmpty(currentMatchId) &&
                generation == generationVersion)
            {
                var seedEventType = string.IsNullOrEmpty(eventType) ? SelectSeedEventType(bank, queue.Count) : eventType;
                var packet = await BuildPacket(BuildSeedSummary(seedEventType, bank), generation, false);
                if (generation != generationVersion)
                    break;
                if (packet == null || packet.Nodes.Count == 0)
                    continue;
                queue.Enqueue(packet);
            }
        }
        finally
        {
            replenishing.Remove(replenishKey);
        }
    }

    private SoccerEventSummary BuildSeedSummary(string eventType, SoccerPacketBank bank)
    {
        var manager = FStudio.MatchEngine.MatchManager.Current;
        var isPregame = bank == SoccerPacketBank.Pregame;
        return new SoccerEventSummary
        {
            MatchId = currentMatchId,
            Phase = isPregame ? SoccerMatchPhase.Pregame : SoccerMatchPhase.Live,
            EventType = eventType,
            Minute = manager != null ? Mathf.CeilToInt(manager.minutes) : 0,
            HomeTeam = homeActor?.Name ?? "Home",
            AwayTeam = awayActor?.Name ?? "Away",
            HomeScore = manager != null ? manager.homeTeamScore : 0,
            AwayScore = manager != null ? manager.awayTeamScore : 0,
            PrimaryActor = string.Empty,
            RawLog = BuildSeedLog(eventType),
            IsHighPriority = bank == SoccerPacketBank.LiveScoreSensitive,
            Priority = bank == SoccerPacketBank.LiveScoreSensitive ? SoccerInterruptPriority.High : SoccerInterruptPriority.Normal,
            Sequence = 0,
            IsScoreSensitive = false,
            WorldFingerprint = $"{eventType}|template",
            CreatedAtUtc = DateTime.UtcNow,
            RecentResidue = matchState?.GetRecentResidue() ?? Array.Empty<string>()
        };
    }

    private async Task<SoccerInterruptPacket> BuildPacket(SoccerEventSummary summary, int generation, bool includeAudio)
    {
        if (!IsCurrent(summary, generation))
            return null;

        var speakers = ResolveSpeakers(summary);
        if (speakers.Length == 0)
            return null;

        var chat = new Chat(new Idea(summary.RawLog ?? summary.ScoreLine), generator.ManagerContext)
        {
            Actors = speakers.Select(actor => new ActorContext(actor)).ToArray(),
            Context = BuildContext(summary, speakers),
            Topic = summary.RawLog ?? summary.ScoreLine
        };

        PublishStatus($"Generating soccer interrupt: {DescribeSummary(summary)}", 6f);

        var prompt = new PromptResolver(generator.ManagerContext, "Soccer Mode", "Dialogue Generation");
        await prompt.Resolve(BuildMatchEvent(summary), string.Join(", ", speakers.Select(actor => actor.Name)));
        if (!IsCurrent(summary, generation))
            return null;

        var content = await LLM.CompleteAsync(prompt, chat, true);
        if (!IsCurrent(summary, generation))
            return null;

        var nodes = ParseNodes(content, speakers);
        if (nodes.Count == 0)
            return null;

        chat.Nodes = nodes;

        if (sentimentTagger != null)
        {
            PublishStatus("Tagging soccer interrupt sentiment", 4f);
            var sentimentPrompt = new PromptResolver(generator.ManagerContext, "Soccer Mode", "Sentiment Tagger");
            foreach (var node in nodes)
            {
                if (!IsCurrent(summary, generation))
                    return null;
                await sentimentTagger.GenerateForNode(sentimentPrompt, chat, node, chat.Names);
            }
        }

        var audioPrepared = false;
        var audioPreparationFailed = false;
        if (includeAudio)
        {
            var packetForAudio = new SoccerInterruptPacket
            {
                MatchId = summary.MatchId,
                SeedEvent = summary,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                IsTemplatePacket = summary.Sequence == 0,
                Nodes = nodes
            };
            audioPrepared = await PreparePacketAudio(packetForAudio, generation, int.MaxValue);
            audioPreparationFailed = packetForAudio.AudioPreparationFailed;
            if (!audioPrepared && audioPreparationFailed)
                return null;
        }

        PublishStatus(includeAudio ? "Soccer interrupt ready with audio" : "Soccer interrupt text ready", 3f);

        return new SoccerInterruptPacket
        {
            PacketId = Guid.NewGuid().ToString("N"),
            MatchId = summary.MatchId,
            TriggerEventType = summary.EventType,
            Bank = policy.GetBank(summary),
            Priority = summary.Priority,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            SeedEvent = summary,
            Sequence = summary.Sequence,
            WorldFingerprint = summary.WorldFingerprint,
            IsScoreSensitive = summary.IsScoreSensitive,
            IsTemplatePacket = summary.Sequence == 0,
            AudioPrepared = audioPrepared,
            AudioPreparationFailed = audioPreparationFailed,
            Nodes = nodes
        };
    }

    private Task<bool> PrepareFirstPacketAudio(SoccerInterruptPacket packet, int generation)
    {
        return PreparePacketAudio(packet, generation, FirstPlayableNodeCount);
    }

    private async Task<bool> PreparePacketAudio(SoccerInterruptPacket packet, int generation, int requiredNodeCount)
    {
        if (packet == null || packet.Nodes == null || packet.Nodes.Count == 0)
            return false;

        if (packet.AudioPrepared)
            return true;

        if (ttsGenerator == null)
        {
            packet.AudioPrepared = true;
            return true;
        }

        var nodes = packet.Nodes.Where(node => node != null).ToList();
        if (nodes.Count == 0)
            return false;

        var requiredCount = Math.Min(Math.Max(requiredNodeCount, 1), nodes.Count);
        var requiresFullPacket = requiredCount >= nodes.Count;

        PublishStatus(requiresFullPacket
            ? $"Generating soccer interrupt audio: {DescribeSummary(packet.SeedEvent)}"
            : $"Generating first soccer interrupt line: {DescribeSummary(packet.SeedEvent)}", 4f);

        packet.AudioPreparationInProgress = true;
        try
        {
            var tasks = new List<Task>();
            for (var i = 0; i < requiredCount; i++)
            {
                if (!IsCurrent(packet.SeedEvent, generation) || IsStale(packet, packet.SeedEvent))
                    return false;

                var node = nodes[i];
                if (node.HasPlayableAudio)
                    continue;

                tasks.Add(ttsGenerator.GenerateTextToSpeech(node));
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);

            if (!IsCurrent(packet.SeedEvent, generation) || IsStale(packet, packet.SeedEvent))
                return false;

            var requiredAudioPrepared = nodes.Take(requiredCount).All(node => node.HasPlayableAudio);
            packet.AudioPrepared = nodes.All(node => node.HasPlayableAudio);
            packet.AudioPreparationFailed = requiresFullPacket ? !packet.AudioPrepared : !requiredAudioPrepared;

            if (packet.AudioPreparationFailed)
                PublishStatus($"Soccer interrupt audio incomplete: {DescribeSummary(packet.SeedEvent)}", 4f);

            return requiresFullPacket ? packet.AudioPrepared : requiredAudioPrepared;
        }
        finally
        {
            packet.AudioPreparationInProgress = false;
        }
    }

    private void PrepareRemainingPacketAudio(SoccerInterruptPacket packet, int generation)
    {
        if (packet == null || packet.AudioPrepared || packet.AudioPreparationInProgress || ttsGenerator == null)
            return;

        _ = PrepareRemainingPacketAudioAsync(packet, generation);
    }

    private async Task PrepareRemainingPacketAudioAsync(SoccerInterruptPacket packet, int generation)
    {
        try
        {
            await PreparePacketAudio(packet, generation, int.MaxValue);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SoccerInterruptService background audio prep failed: {e}");
        }
    }

    private Actor[] ResolveSpeakers(SoccerEventSummary summary)
    {
        var chat = ChatManager.Instance?.NowPlaying;
        if (chat?.Actors == null || chat.Actors.Length == 0)
            return Array.Empty<Actor>();

        var actors = chat.Actors
            .Select(actor => actor.Reference)
            .Where(actor => actor != null)
            .ToList();

        var selected = new List<Actor>();
        TryAdd(selected, actors, homeActor);
        TryAdd(selected, actors, awayActor);
        TryAdd(selected, actors, actors.FirstOrDefault(actor => actor.Name == "United Nations"));

        foreach (var securityCouncil in new[] { "America", "Britain", "France", "Russia", "China" })
            TryAdd(selected, actors, actors.FirstOrDefault(actor => actor.Name == securityCouncil));

        if (!string.IsNullOrWhiteSpace(summary.PrimaryActor))
            TryAdd(selected, actors, actors.FirstOrDefault(actor => actor.Aliases.Contains(summary.PrimaryActor)));

        foreach (var actor in actors)
        {
            if (selected.Count >= 3)
                break;
            TryAdd(selected, actors, actor);
        }

        return selected.Take(3).ToArray();
    }

    private static void TryAdd(List<Actor> selected, List<Actor> available, Actor actor)
    {
        if (actor == null || !available.Contains(actor) || selected.Contains(actor))
            return;
        selected.Add(actor);
    }

    private List<ChatNode> ParseNodes(string content, IEnumerable<Actor> speakers)
    {
        var available = speakers.ToList();
        var nodes = new List<ChatNode>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(':');
            if (parts.Length < 2)
                continue;

            var name = parts[0].Trim().Replace("**", string.Empty);
            var actor = available.FirstOrDefault(a => a.Aliases.Contains(name));
            if (actor == null)
                continue;

            var text = string.Join(":", parts.Skip(1)).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            nodes.Add(new ChatNode(actor, text)
            {
                Notes = InterruptNodeMarker
            });
        }

        return nodes;
    }

    private string BuildMatchEvent(SoccerEventSummary summary)
    {
        return
            $"Event Type: {summary.EventType}\n" +
            $"Minute: {summary.Minute}\n" +
            $"Score: {summary.ScoreLine}\n" +
            $"Primary Actor: {summary.PrimaryActor}\n" +
            $"Match Log: {summary.RawLog}\n" +
            $"Recent Residue:\n- {string.Join("\n- ", summary.RecentResidue ?? Array.Empty<string>())}";
    }

    private void PublishStatus(string message, float lifetimeSeconds)
    {
        if (generator?.ManagerContext == null || string.IsNullOrWhiteSpace(message))
            return;

        UiEventBus.Publish(generator.ManagerContext, message, lifetimeSeconds);
    }

    private static string DescribeSummary(SoccerEventSummary summary)
    {
        if (summary == null)
            return "match event";

        if (summary.IsScoreSensitive)
            return $"{summary.EventType} ({summary.ScoreLine})";

        return string.IsNullOrWhiteSpace(summary.EventType) ? "match event" : summary.EventType;
    }

    private string BuildContext(SoccerEventSummary summary, IEnumerable<Actor> speakers)
    {
        var affinity = string.Join("\n", speakers.Select(actor => $"{actor.Name}: {BuildAffinity(actor, summary)}"));
        return
            $"Live soccer interrupt.\n" +
            $"Home: {summary.HomeTeam}\n" +
            $"Away: {summary.AwayTeam}\n" +
            $"Minute: {summary.Minute}\n" +
            $"Score: {summary.ScoreLine}\n" +
            $"Priority: {(summary.IsHighPriority ? "high" : "normal")}\n" +
            $"Actor Affinity:\n{affinity}\n" +
            $"Recent event: {summary.RawLog}";
    }

    private string BuildAffinity(Actor actor, SoccerEventSummary summary)
    {
        if (actor == null)
            return "observer";
        if (actor == homeActor)
            return "direct stakeholder: home side";
        if (actor == awayActor)
            return "direct stakeholder: away side";
        if (actor.Name == "United Nations")
            return "bureaucratic host trying to preserve legitimacy";
        if (new[] { "America", "Britain", "France", "Russia", "China" }.Contains(actor.Name))
            return "security council / referee-adjacent power reacting to legitimacy and procedure";
        return "nearby observer with scene continuity exposure";
    }

    private bool IsCurrent(SoccerEventSummary summary, int generation)
    {
        return summary != null &&
            generation == generationVersion &&
            !string.IsNullOrEmpty(currentMatchId) &&
            summary.MatchId == currentMatchId;
    }

    private bool IsStale(SoccerInterruptPacket packet, SoccerEventSummary summary)
    {
        if (packet == null)
            return true;

        if (string.IsNullOrEmpty(currentMatchId) || packet.MatchId != currentMatchId)
        {
            packet.Superseded = true;
            return true;
        }

        if (packet.IsTemplatePacket)
            return false;

        if (packet.ExpiresAtUtc <= DateTime.UtcNow)
        {
            packet.Superseded = true;
            return true;
        }

        // Match events arrive much faster than LLM/TTS can finish. A newer event does not
        // make this packet stale unless the packet depends on world state that changed.
        if (!packet.IsScoreSensitive || matchState == null)
            return false;

        var currentFingerprint = matchState.BuildCurrentWorldFingerprint(summary.EventType, true);
        if (packet.WorldFingerprint == currentFingerprint)
            return false;

        packet.Superseded = true;
        return true;
    }

    private int CountReady(SoccerPacketBank bank)
    {
        if (!packetsByBank.TryGetValue(bank, out var queue))
            return 0;

        return queue.Count(packet =>
            packet != null &&
            !packet.Consumed &&
            !packet.Superseded &&
            packet.ExpiresAtUtc > DateTime.UtcNow);
    }

    private static int CountReady(Queue<SoccerInterruptPacket> queue, string eventType = null)
    {
        if (queue == null)
            return 0;

        return queue.Count(packet =>
            packet != null &&
            !packet.Consumed &&
            !packet.Superseded &&
            packet.ExpiresAtUtc > DateTime.UtcNow &&
            (string.IsNullOrEmpty(eventType) || string.Equals(packet.TriggerEventType, eventType, StringComparison.Ordinal)));
    }

    private static void PruneQueue(Queue<SoccerInterruptPacket> queue)
    {
        if (queue == null || queue.Count == 0)
            return;

        var keep = queue
            .Where(packet =>
                packet != null &&
                !packet.Consumed &&
                !packet.Superseded &&
                packet.ExpiresAtUtc > DateTime.UtcNow)
            .ToArray();

        queue.Clear();
        for (var i = 0; i < keep.Length; i++)
            queue.Enqueue(keep[i]);
    }

    private int GetBankTarget(SoccerPacketBank bank)
    {
        return bank switch
        {
            SoccerPacketBank.Pregame => 2,
            SoccerPacketBank.LiveScoreSensitive => 2,
            SoccerPacketBank.LiveGeneric => 3,
            SoccerPacketBank.Broadcast => 1,
            _ => 1
        };
    }

    private string SelectSeedEventType(SoccerPacketBank bank, int index)
    {
        var candidates = seedEventTypes
            .Where(eventType => GetBankForSeed(eventType) == bank)
            .ToArray();

        if (candidates.Length == 0)
            return nameof(RefereeShortWhistleEvent);

        return candidates[index % candidates.Length];
    }

    private SoccerPacketBank GetBankForSeed(string eventType)
    {
        if (policy.IsPregameEvent(eventType))
            return SoccerPacketBank.Pregame;

        if (eventType == nameof(GoalScoredEvent))
            return SoccerPacketBank.LiveScoreSensitive;

        return SoccerPacketBank.LiveGeneric;
    }

    private static string BuildReplenishKey(SoccerPacketBank bank, string eventType)
    {
        return string.IsNullOrEmpty(eventType) ? $"{bank}:*" : $"{bank}:{eventType}";
    }

    private bool TryInjectPacket(SoccerInterruptPacket packet)
    {
        if (packet == null || IsStale(packet, packet.SeedEvent))
            return false;

        RemoveSupersededInterruptNodes(packet);

        return ChatManager.Instance != null &&
            ChatManager.Instance.InjectNodes(ChatManager.Instance.NowPlaying, packet.Nodes);
    }

    private bool CanInjectNow()
    {
        return CanInjectNow(null);
    }

    private bool CanInjectNow(SoccerInterruptPacket packet)
    {
        if (string.IsNullOrEmpty(currentMatchId))
            return false;

        var manager = ChatManager.Instance;
        if (manager == null || !manager.ReadyForAction || ChatManager.IsPaused)
            return false;

        var chat = manager.NowPlaying;
        if (chat == null)
            return false;

        if (!HasQueuedInterruptNodes(chat))
            return true;

        return packet != null && CanSupersedeQueuedInterrupt(packet);
    }

    private void ApplySummary(SoccerInterruptPacket packet, SoccerEventSummary summary)
    {
        if (packet == null || summary == null)
            return;

        packet.MatchId = summary.MatchId;
        packet.TriggerEventType = summary.EventType;
        packet.Bank = policy.GetBank(summary);
        packet.Priority = summary.Priority;
        packet.SeedEvent = summary;
        packet.Sequence = summary.Sequence;
        packet.WorldFingerprint = summary.WorldFingerprint;
        packet.IsScoreSensitive = summary.IsScoreSensitive;
        packet.IsTemplatePacket = false;
    }

    private void QueuePending(SoccerInterruptPacket packet)
    {
        if (packet == null)
            return;

        if (pendingPacket == null || policy.ShouldSupersede(packet, pendingPacket))
        {
            if (pendingPacket != null)
                pendingPacket.Superseded = true;
            pendingPacket = packet;
        }
        else
        {
            packet.Superseded = true;
        }
    }

    private void QueueGenerationSummary(SoccerEventSummary summary)
    {
        if (summary == null)
            return;

        if (queuedGenerationSummary == null || ShouldSupersede(summary, queuedGenerationSummary))
            queuedGenerationSummary = summary;
    }

    private void TryStartQueuedGeneration(int generation)
    {
        var summary = queuedGenerationSummary;
        queuedGenerationSummary = null;

        if (summary == null || generation != generationVersion || !IsCurrent(summary, generation))
            return;

        _ = TryInject(summary);
    }

    private static bool ShouldSupersede(SoccerEventSummary candidate, SoccerEventSummary existing)
    {
        if (candidate == null)
            return false;
        if (existing == null)
            return true;

        if (candidate.Priority > existing.Priority)
            return true;
        if (candidate.Priority < existing.Priority)
            return false;

        if (candidate.IsScoreSensitive && !existing.IsScoreSensitive)
            return true;
        if (!candidate.IsScoreSensitive && existing.IsScoreSensitive)
            return false;

        if (candidate.Sequence > existing.Sequence)
            return true;
        if (candidate.Sequence < existing.Sequence)
            return false;

        return candidate.CreatedAtUtc >= existing.CreatedAtUtc;
    }

    private void RemoveQueuedInterruptNodes()
    {
        var chat = ChatManager.Instance?.NowPlaying;
        if (chat?.Nodes == null)
            return;

        RemoveQueuedInterruptNodes(chat);
    }

    private void RemoveSupersededInterruptNodes(SoccerInterruptPacket packet)
    {
        if (packet == null || !CanSupersedeQueuedInterrupt(packet))
            return;

        var manager = ChatManager.Instance;
        manager?.InterruptActiveNode(InterruptNodeMarker);

        var chat = manager?.NowPlaying;
        if (chat?.Nodes == null)
            return;

        RemoveQueuedInterruptNodes(chat);
    }

    private static bool HasQueuedInterruptNodes(Chat chat)
    {
        return chat?.Nodes != null &&
            chat.Nodes.Any(node => node != null && node.New && node.Notes == InterruptNodeMarker);
    }

    private bool CanSupersedeQueuedInterrupt(SoccerInterruptPacket packet)
    {
        return packet != null && !packet.IsTemplatePacket && packet.Sequence > latestInjectedSequence;
    }

    private static void RemoveQueuedInterruptNodes(Chat chat)
    {
        if (chat?.Nodes == null)
            return;

        for (var i = chat.Nodes.Count - 1; i >= 0; i--)
        {
            var node = chat.Nodes[i];
            if (node == null || !node.New || node.Notes != InterruptNodeMarker)
                continue;

            node.ReleaseRuntimeAudio();
            chat.Nodes.RemoveAt(i);
        }
    }

    private void FlushQueuedPackets(SoccerEventSummary summary)
    {
        if (pendingPacket == null)
            return;

        if (summary == null)
            return;

        if (IsStale(pendingPacket, pendingPacket.SeedEvent) ||
            (summary.IsScoreSensitive && pendingPacket.IsScoreSensitive && summary.Sequence > pendingPacket.Sequence))
        {
            pendingPacket.Superseded = true;
            pendingPacket = null;
        }
    }

    private string BuildSeedLog(string eventType)
    {
        return GetConfiguredSeed(eventType) ?? eventType switch
        {
            "Pregame" =>
                "Kickoff is approaching. Deliver a very short pregame beat about nerves, legitimacy, or rivalries before play starts.",
            nameof(GoalScoredEvent) =>
                "A goal has just been scored. React briefly without naming a specific score or minute.",
            nameof(RefereeShortWhistleEvent) =>
                "The referee has interrupted play. React briefly to procedure, officiating, or legitimacy.",
            nameof(KeeperSavesTheBallEvent) =>
                "A dramatic save has just happened. React briefly without naming a specific score or minute.",
            nameof(BallHitTheWoodWorkEvent) =>
                "A near miss just rattled the stadium. React briefly without naming a specific score or minute.",
            nameof(PlayerSlideTackleEvent) =>
                "A hard tackle just changed the temperature of the match. React briefly.",
            _ => $"A live {eventType} reaction. React briefly without citing exact numbers."
        };
    }

    private string GetConfiguredSeed(string eventType)
    {
        if (!interruptSeeds.TryGetValue(eventType, out var variants) || variants == null || variants.Length == 0)
            return null;

        return variants.Sample();
    }
}
