using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class FolderSource : MonoBehaviour, IConfigurable<FolderConfigs>
{
    private static readonly object replaySnapshotLock = new object();
    private static readonly Dictionary<string, List<ReplayStatusRecord>> replaySnapshots = new Dictionary<string, List<ReplayStatusRecord>>(StringComparer.OrdinalIgnoreCase);
    private static readonly object instanceLock = new object();
    private static readonly Dictionary<string, FolderSource> instances = new Dictionary<string, FolderSource>(StringComparer.OrdinalIgnoreCase);
    private const float SweepIntervalSeconds = 15f;

    public string ReplayDirectory;
    public int ReplayRate = 80;
    public int ReplaysPerBatch = 20;
    public int MaxReplayAgeInMinutes = 1440;

    private List<string> replays = new List<string>();
    private string fileName;
    private string manifestPath;
    private ChatManagerContext boundContext;
    private bool subscribedToQueueEmpty;
    private bool subscribedToRuntimeEvents;
    private ReplayManifest manifest = new ReplayManifest();
    private readonly HashSet<string> knownReplayFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool isSweeping;
    private Coroutine sweepCoroutine;
    private Coroutine replayCoroutine;

    public void Configure(FolderConfigs c)
    {
        UnsubscribeFromQueueEmpty();
        UnsubscribeFromRuntimeEvents();

        if (replayCoroutine != null)
        {
            StopCoroutine(replayCoroutine);
            replayCoroutine = null;
        }

        if (sweepCoroutine != null)
        {
            StopCoroutine(sweepCoroutine);
            sweepCoroutine = null;
        }

        ReplayDirectory = c.ReplayDirectory;
        ReplayRate = c.ReplayRate;
        ReplaysPerBatch = c.ReplaysPerBatch;
        MaxReplayAgeInMinutes = c.MaxReplayAgeInMinutes;

        if (MaxReplayAgeInMinutes < 1)
            MaxReplayAgeInMinutes = 1440 * 365;

        replays = LoadReplays();
        manifest = LoadManifest();
        BootstrapManifest();
        PrimeKnownReplayFiles();

        SubscribeToQueueEmpty();
        SubscribeToRuntimeEvents();

        sweepCoroutine = StartCoroutine(SweepForIncomingEpisodes());

        ReplayNewEpisode();
    }

    public void ReplayNewEpisode()
    {
        if (!CanFeedPlayback())
            return;
        if (replayCoroutine != null)
            return;

        replayCoroutine = StartCoroutine(ReplayEpisodes());
    }

    private IEnumerator ReplayEpisodes()
    {
        try
        {
            yield return new WaitUntil(() => ChatManager.Instance != null && ChatManager.Instance.ReadyForAction);

            if (!CanFeedPlayback())
                yield break;

            yield return FetchFiles(ReplaysPerBatch).AsCoroutine();
        }
        finally
        {
            replayCoroutine = null;
        }
    }

    private void Start()
    {
        boundContext = ChatManagerContext.Current;
        if (boundContext == null)
            return;

        RegisterInstance();
        boundContext.ConfigManager.RegisterConfig(typeof(FolderConfigs), "folder", (_config) => Configure((FolderConfigs)_config));
    }

    private void OnDestroy()
    {
        UnregisterInstance();
        UnsubscribeFromQueueEmpty();
        UnsubscribeFromRuntimeEvents();
        replayCoroutine = null;
        sweepCoroutine = null;
        StopAllCoroutines();
    }

    private async Task FetchFiles(int count)
    {
        if (!CanFeedPlayback())
            return;

        var path = GetReplayDirectoryPath();

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        BootstrapManifest();
        var allEntries = GetCandidateEntries(path).ToList();
        count = Mathf.Min(count, allEntries.Count);
        if (count == 0)
            return;

        var tasks = new List<Task>();
        var attempts = 0;

        do
        {
            if (!CanFeedPlayback())
                return;

            var selected = GetNextReplayCandidates(allEntries)
                .Take(count)
                .Select(entry => LogThenLoad(entry.slug))
                .ToList();

            foreach (var task in selected)
                if (await task)
                    tasks.Add(task);
            attempts++;
        } while (tasks.Count < count && attempts < 3);

        if (tasks.Count > 0 && boundContext != null)
            UiEventBus.Publish(boundContext, $"Loaded {tasks.Count} replay{(tasks.Count == 1 ? "" : "s")}");
    }

    private IEnumerator SweepForIncomingEpisodes()
    {
        while (Application.isPlaying)
        {
            yield return new WaitForSeconds(SweepIntervalSeconds);

            if (isSweeping || boundContext == null || string.IsNullOrWhiteSpace(ReplayDirectory))
                continue;

            isSweeping = true;
            yield return SweepForNewFiles().AsCoroutine();
            isSweeping = false;
        }
    }

    private async Task SweepForNewFiles()
    {
        var path = GetReplayDirectoryPath();
        if (!Directory.Exists(path))
            return;

        var discovered = Directory.GetFiles(path, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(slug => !knownReplayFiles.Contains(slug))
            .ToList();

        if (discovered.Count == 0)
            return;

        BootstrapManifest();

        foreach (var slug in discovered)
            knownReplayFiles.Add(slug);

        if (!CanFeedPlayback())
            return;

        var autoQueued = 0;
        foreach (var slug in discovered.Take(ReplaysPerBatch))
        {
            if (!CanFeedPlayback())
                return;

            if (await LogThenLoad(slug))
                autoQueued++;
        }

        if (autoQueued > 0 && boundContext != null)
            UiEventBus.Publish(boundContext, $"Detected {autoQueued} incoming episode{(autoQueued == 1 ? "" : "s")}");
    }

    private async Task<bool> LogThenLoad(string title, int attempts = 0)
    {
        if (attempts > 3) return false;
        if (!CanFeedPlayback())
            return false;
        try
        {
            var chat = await Chat.Load(ReplayDirectory, title);
            if (!CanFeedPlayback())
                return false;
            AddReplayToList(title);
            ChatManager.Instance.AddToPlayList(chat);
            return true;
        }
        catch (JsonException)
        {
            Chat.Delete(ReplayDirectory, title);
            AddReplayToList(title);
            return false;
        }
        catch (IOException)
        {
            return await LogThenLoad(title, ++attempts);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            AddReplayToList(title);
            return false;
        }
    }

    private List<string> LoadReplays()
    {
        fileName = $"replays-{boundContext?.Key ?? ChatManagerContext.Current?.Key}.txt";
        if (!File.Exists(fileName))
            return new List<string>();
        return File.ReadAllLines(fileName)
            .ToList();
    }

    private void AddReplayToList(string title)
    {
        replays.Add(title);
        File.WriteAllLinesAsync(fileName, replays
            .Distinct()
            .TakeLast(ReplayRate)
            .ToList());
    }

    private void SubscribeToQueueEmpty()
    {
        if (boundContext == null)
            return;
        if (subscribedToQueueEmpty)
            return;

        boundContext.OnChatQueueEmpty += ReplayNewEpisode;
        subscribedToQueueEmpty = true;
    }

    private bool CanFeedPlayback()
    {
        if (boundContext == null || ChatManager.Instance == null)
            return false;

        var currentContext = ChatManager.Instance.CurrentContext;
        if (currentContext == null)
            return false;

        return string.Equals(boundContext.Key, currentContext.Key, StringComparison.OrdinalIgnoreCase);
    }

    private void SubscribeToRuntimeEvents()
    {
        if (boundContext == null)
            return;
        if (subscribedToRuntimeEvents)
            return;

        boundContext.OnChatQueueAdded += OnChatQueued;
        boundContext.OnChatLoaded += OnChatLoaded;
        subscribedToRuntimeEvents = true;
    }

    private void UnsubscribeFromRuntimeEvents()
    {
        if (boundContext == null || !subscribedToRuntimeEvents)
            return;

        boundContext.OnChatQueueAdded -= OnChatQueued;
        boundContext.OnChatLoaded -= OnChatLoaded;
        subscribedToRuntimeEvents = false;
    }

    private void UnsubscribeFromQueueEmpty()
    {
        if (boundContext == null || !subscribedToQueueEmpty)
            return;

        boundContext.OnChatQueueEmpty -= ReplayNewEpisode;
        subscribedToQueueEmpty = false;
    }

    private void OnChatQueued(Chat chat)
    {
        if (chat == null || boundContext == null || chat.Key != boundContext.Key || !chat.NewEpisode)
            return;

        knownReplayFiles.Add(chat.FileName);
        UpsertManifestEntry(chat.FileName, entry =>
        {
            entry.slug = chat.FileName;
            entry.title = chat.Title ?? chat.FileName ?? string.Empty;
            entry.generatedAt = DateTimeOffset.Now.ToString("O");
            entry.replayEligibleAt = entry.generatedAt;
            entry.source = chat.Idea?.Source ?? string.Empty;
            entry.voteScore = CalculateVoteScore(entry.upVotes, entry.downVotes);
            entry.lastSeenFile = GetChatPath(chat.FileName);
        });
    }

    private void OnChatLoaded(Chat chat)
    {
        if (chat == null || boundContext == null || chat.Key != boundContext.Key || chat.NewEpisode)
            return;

        var now = DateTimeOffset.Now;
        UpsertManifestEntry(chat.FileName, entry =>
        {
            entry.slug = chat.FileName;
            entry.title = chat.Title ?? chat.FileName ?? string.Empty;
            entry.lastPlayedAt = now.ToString("O");
            entry.replayEligibleAt = now.AddMinutes(GetReplayCooldownMinutes()).ToString("O");
            entry.timesReplayed = Mathf.Max(0, entry.timesReplayed) + 1;
            entry.voteScore = CalculateVoteScore(entry.upVotes, entry.downVotes);
            entry.lastSeenFile = GetChatPath(chat.FileName);
            if (string.IsNullOrWhiteSpace(entry.generatedAt))
                entry.generatedAt = now.ToString("O");
        });
    }

    private void RegisterInstance()
    {
        if (boundContext == null || string.IsNullOrWhiteSpace(boundContext.Key))
            return;

        lock (instanceLock)
            instances[boundContext.Key] = this;
    }

    private void UnregisterInstance()
    {
        if (boundContext == null || string.IsNullOrWhiteSpace(boundContext.Key))
            return;

        lock (instanceLock)
        {
            if (instances.TryGetValue(boundContext.Key, out var current) && current == this)
                instances.Remove(boundContext.Key);
        }
    }

    private string GetReplayDirectoryPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, ReplayDirectory);
    }

    private string GetChatPath(string slug)
    {
        return Path.Combine(GetReplayDirectoryPath(), $"{slug}.json");
    }

    private ReplayManifest LoadManifest()
    {
        manifestPath = Path.Combine(GetReplayDirectoryPath(), ".hbox-replays");

        try
        {
            if (!File.Exists(manifestPath))
                return new ReplayManifest();

            var json = File.ReadAllText(manifestPath);
            return JsonConvert.DeserializeObject<ReplayManifest>(json) ?? new ReplayManifest();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"FolderSource.LoadManifest failed for '{manifestPath}': {e.Message}");
            return new ReplayManifest();
        }
    }

    private void BootstrapManifest()
    {
        var path = GetReplayDirectoryPath();
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        var changed = false;
        foreach (var file in Directory.GetFiles(path, "*.json"))
        {
            var slug = Path.GetFileNameWithoutExtension(file);
            var entry = manifest.entries.FirstOrDefault(e => string.Equals(e.slug, slug, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new ReplayManifestEntry
                {
                    slug = slug,
                    title = slug,
                    generatedAt = File.GetCreationTimeUtc(file).ToString("O"),
                    replayEligibleAt = File.GetCreationTimeUtc(file).ToString("O"),
                    lastSeenFile = file
                };
                manifest.entries.Add(entry);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(entry.lastSeenFile) || !string.Equals(entry.lastSeenFile, file, StringComparison.OrdinalIgnoreCase))
            {
                entry.lastSeenFile = file;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(entry.generatedAt))
            {
                entry.generatedAt = File.GetCreationTimeUtc(file).ToString("O");
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(entry.replayEligibleAt))
            {
                entry.replayEligibleAt = entry.generatedAt;
                changed = true;
            }

            if (entry.voteScore != CalculateVoteScore(entry.upVotes, entry.downVotes))
            {
                entry.voteScore = CalculateVoteScore(entry.upVotes, entry.downVotes);
                changed = true;
            }
        }

        manifest.entries = manifest.entries
            .Where(e => !string.IsNullOrWhiteSpace(e.slug))
            .Where(e => File.Exists(GetChatPath(e.slug)))
            .GroupBy(e => e.slug, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        PublishReplaySnapshot();

        if (changed)
            SaveManifest();
    }

    private void PrimeKnownReplayFiles()
    {
        knownReplayFiles.Clear();
        foreach (var entry in manifest.entries)
            if (!string.IsNullOrWhiteSpace(entry.slug))
                knownReplayFiles.Add(entry.slug);
    }

    private IEnumerable<ReplayManifestEntry> GetCandidateEntries(string directoryPath)
    {
        var cutoff = DateTimeOffset.Now.AddMinutes(-MaxReplayAgeInMinutes);
        var entries = manifest.entries
            .Where(e => !string.IsNullOrWhiteSpace(e.slug))
            .Where(e => File.Exists(Path.Combine(directoryPath, $"{e.slug}.json")))
            .ToList();

        var recent = entries
            .Where(e => ParseTimestamp(e.generatedAt) >= cutoff)
            .ToList();

        return recent.Count > 0 ? recent : entries;
    }

    private IEnumerable<ReplayManifestEntry> RankReplayCandidates(IEnumerable<ReplayManifestEntry> entries)
    {
        var now = DateTimeOffset.Now;
        var ranked = entries
            .OrderBy(e => ParseTimestamp(e.replayEligibleAt) > now ? 1 : 0)
            .ThenByDescending(e => ComputePriorityScore(e, now, MaxReplayAgeInMinutes / 60f))
            .ThenBy(e => ParseTimestamp(e.replayEligibleAt))
            .ThenBy(e => e.timesReplayed)
            .ThenBy(e => ParseTimestamp(e.lastPlayedAt))
            .ThenByDescending(e => ParseTimestamp(e.generatedAt))
            .ToList();

        return ranked;
    }

    private IReadOnlyList<ReplayManifestEntry> GetNextReplayCandidates(IEnumerable<ReplayManifestEntry> entries)
    {
        var ranked = RankReplayCandidates(entries).ToList();
        var recentHistory = replays.Distinct().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unplayed = ranked
            .Where(entry => !recentHistory.Contains(entry.slug))
            .ToList();

        return unplayed.Count > 0 ? unplayed : ranked;
    }

    private void UpsertManifestEntry(string slug, Action<ReplayManifestEntry> apply)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return;

        var entry = manifest.entries.FirstOrDefault(e => string.Equals(e.slug, slug, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            entry = new ReplayManifestEntry { slug = slug };
            manifest.entries.Add(entry);
        }

        apply(entry);
        entry.voteScore = CalculateVoteScore(entry.upVotes, entry.downVotes);
        SaveManifest();
    }

    private void SaveManifest()
    {
        try
        {
            var path = GetReplayDirectoryPath();
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            manifest.entries = manifest.entries
                .Where(e => !string.IsNullOrWhiteSpace(e.slug))
                .GroupBy(e => e.slug, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(manifestPath, json);
            PublishReplaySnapshot();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"FolderSource.SaveManifest failed for '{manifestPath}': {e.Message}");
        }
    }

    private void PublishReplaySnapshot()
    {
        if (boundContext == null)
            return;

        var path = GetReplayDirectoryPath();
        var now = DateTimeOffset.Now;
        var snapshot = GetNextReplayCandidates(GetCandidateEntries(path))
            .Select(e => BuildReplayStatusRecord(e, now))
            .Where(r => r != null)
            .Select((record, index) =>
            {
                record.nextPlayOrder = index + 1;
                return record;
            })
            .ToList();

        lock (replaySnapshotLock)
            replaySnapshots[boundContext.Key] = snapshot;
    }

    private int GetReplayCooldownMinutes()
    {
        return Mathf.Clamp(MaxReplayAgeInMinutes / 24, 10, 240);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed;
        return DateTimeOffset.MinValue;
    }

    private static int CalculateVoteScore(int upVotes, int downVotes)
    {
        return Mathf.Max(0, upVotes) - Mathf.Max(0, downVotes);
    }

    private static float ComputePriorityScore(ReplayManifestEntry entry, DateTimeOffset now, float hours)
    {
        if (entry == null)
            return 0f;

        var voteScore = entry.voteScore;
        var recencyWeight = ComputeRecencyWeight(ParseTimestamp(entry.lastPlayedAt), now, hours);
        var freshnessBonus = ComputeFreshnessBonus(ParseTimestamp(entry.generatedAt), now);
        return (voteScore * recencyWeight * 10f) - (Mathf.Max(0, entry.timesReplayed) * 2f) + freshnessBonus;
    }

    private static float ComputeRecencyWeight(DateTimeOffset lastPlayedAt, DateTimeOffset now, float hours)
    {
        if (lastPlayedAt == DateTimeOffset.MinValue)
            return 1f;

        var hoursSinceLastPlayed = Mathf.Max(0f, (float)(now - lastPlayedAt).TotalHours);
        return Mathf.Clamp(1f - (hoursSinceLastPlayed / hours), 0.15f, 1f);
    }

    private static float ComputeFreshnessBonus(DateTimeOffset generatedAt, DateTimeOffset now)
    {
        if (generatedAt == DateTimeOffset.MinValue)
            return 0f;

        var daysOld = Mathf.Max(0f, (float)(now - generatedAt).TotalDays);
        return Mathf.Clamp(5f - daysOld, 0f, 5f);
    }

    private ReplayStatusRecord BuildReplayStatusRecord(ReplayManifestEntry entry)
    {
        return BuildReplayStatusRecord(entry, DateTimeOffset.Now);
    }

    private ReplayStatusRecord BuildReplayStatusRecord(ReplayManifestEntry entry, DateTimeOffset now)
    {
        if (entry == null || boundContext == null)
            return null;

        return new ReplayStatusRecord
        {
            slug = entry.slug,
            title = entry.title,
            channelKey = boundContext.Key,
            context = boundContext.Name,
            generatedAt = entry.generatedAt,
            lastPlayedAt = entry.lastPlayedAt,
            replayEligibleAt = entry.replayEligibleAt,
            timesReplayed = entry.timesReplayed,
            upVotes = entry.upVotes,
            downVotes = entry.downVotes,
            voteScore = entry.voteScore,
            lastVoteAt = entry.lastVoteAt,
            discordMessageId = entry.discordMessageId,
            discordChannelId = entry.discordChannelId,
            priorityScore = ComputePriorityScore(entry, now, MaxReplayAgeInMinutes / 60f),
            eligibleNow = ParseTimestamp(entry.replayEligibleAt) <= now
        };
    }

    private ReplayStatusRecord RecordDiscordMessageInternal(string slug, DiscordPostedMessage message)
    {
        ReplayStatusRecord result = null;
        UpsertManifestEntry(slug, entry =>
        {
            entry.discordMessageId = message?.id ?? string.Empty;
            entry.discordChannelId = message?.channel_id ?? string.Empty;
            result = BuildReplayStatusRecord(entry);
        });
        return result;
    }

    private ReplayStatusRecord ApplyVoteInternal(string slug, int upDelta, int downDelta, string source, string messageId)
    {
        ReplayStatusRecord result = null;
        var now = DateTimeOffset.Now.ToString("O");

        UpsertManifestEntry(slug, entry =>
        {
            entry.upVotes = Mathf.Max(0, entry.upVotes + upDelta);
            entry.downVotes = Mathf.Max(0, entry.downVotes + downDelta);

            entry.voteScore = CalculateVoteScore(entry.upVotes, entry.downVotes);
            entry.lastVoteAt = now;

            if (!string.IsNullOrWhiteSpace(messageId))
                entry.discordMessageId = messageId;
            if (!string.IsNullOrWhiteSpace(source))
                entry.voteSource = source;

            result = BuildReplayStatusRecord(entry);
        });

        return result;
    }

    public static IReadOnlyList<ReplayStatusRecord> GetReplayStatus(string channelKey = null, int limit = 50)
    {
        lock (replaySnapshotLock)
        {
            IEnumerable<ReplayStatusRecord> rows = replaySnapshots.Values.SelectMany(v => v);
            if (!string.IsNullOrWhiteSpace(channelKey))
                rows = rows.Where(r => string.Equals(r.channelKey, channelKey, StringComparison.OrdinalIgnoreCase));
            else
                rows = rows
                    .OrderBy(r => r.nextPlayOrder)
                    .ThenBy(r => r.context)
                    .ThenBy(r => r.channelKey);

            return rows
                .Take(Mathf.Clamp(limit, 1, 200))
                .Select(CloneReplayStatusRecord)
                .ToList();
        }
    }

    private static ReplayStatusRecord CloneReplayStatusRecord(ReplayStatusRecord record)
    {
        return new ReplayStatusRecord
        {
            slug = record.slug,
            title = record.title,
            channelKey = record.channelKey,
            context = record.context,
            generatedAt = record.generatedAt,
            lastPlayedAt = record.lastPlayedAt,
            replayEligibleAt = record.replayEligibleAt,
            timesReplayed = record.timesReplayed,
            upVotes = record.upVotes,
            downVotes = record.downVotes,
            voteScore = record.voteScore,
            lastVoteAt = record.lastVoteAt,
            discordMessageId = record.discordMessageId,
            discordChannelId = record.discordChannelId,
            priorityScore = record.priorityScore,
            eligibleNow = record.eligibleNow,
            nextPlayOrder = record.nextPlayOrder
        };
    }

    public static ReplayStatusRecord RecordDiscordMessage(string channelKey, string slug, DiscordPostedMessage message)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(slug) || message == null || string.IsNullOrWhiteSpace(message.id))
            return null;

        lock (instanceLock)
        {
            if (!instances.TryGetValue(channelKey, out var source))
                return null;

            return source.RecordDiscordMessageInternal(slug, message);
        }
    }

    public static ReplayStatusRecord ApplyVote(string channelKey, string slug, int delta, string source = null, string messageId = null)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(slug) || delta == 0)
            return null;

        var upDelta = delta > 0 ? delta : 0;
        var downDelta = delta < 0 ? -delta : 0;
        return ApplyVote(channelKey, slug, upDelta, downDelta, source, messageId);
    }

    public static ReplayStatusRecord ApplyVote(string channelKey, string slug, int upDelta, int downDelta, string source = null, string messageId = null)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(slug))
            return null;
        if (upDelta == 0 && downDelta == 0)
            return null;

        lock (instanceLock)
        {
            if (!instances.TryGetValue(channelKey, out var replaySource))
                return null;

            return replaySource.ApplyVoteInternal(slug, upDelta, downDelta, source, messageId);
        }
    }

    public static ReplayDiscordBinding FindReplayByDiscordMessage(string messageId, string discordChannelId = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return null;

        lock (instanceLock)
        {
            foreach (var pair in instances)
            {
                var source = pair.Value;
                if (source?.manifest?.entries == null)
                    continue;

                var entry = source.manifest.entries.FirstOrDefault(e =>
                    string.Equals(e.discordMessageId, messageId, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(discordChannelId) || string.Equals(e.discordChannelId, discordChannelId, StringComparison.OrdinalIgnoreCase)));

                if (entry == null)
                    continue;

                return new ReplayDiscordBinding
                {
                    channelKey = pair.Key,
                    slug = entry.slug,
                    discordMessageId = entry.discordMessageId,
                    discordChannelId = entry.discordChannelId
                };
            }
        }

        return null;
    }
}

[Serializable]
public sealed class ReplayManifest
{
    public List<ReplayManifestEntry> entries = new List<ReplayManifestEntry>();
}

[Serializable]
public sealed class ReplayManifestEntry
{
    public string slug;
    public string title;
    public string source;
    public string generatedAt;
    public string lastPlayedAt;
    public string replayEligibleAt;
    public int timesReplayed;
    public string lastSeenFile;
    public int upVotes;
    public int downVotes;
    public int voteScore;
    public string lastVoteAt;
    public string voteSource;
    public string discordMessageId;
    public string discordChannelId;
}

[Serializable]
public sealed class ReplayStatusRecord
{
    public string slug;
    public string title;
    public string channelKey;
    public string context;
    public string generatedAt;
    public string lastPlayedAt;
    public string replayEligibleAt;
    public int timesReplayed;
    public int upVotes;
    public int downVotes;
    public int voteScore;
    public string lastVoteAt;
    public string discordMessageId;
    public string discordChannelId;
    public float priorityScore;
    public bool eligibleNow;
    public int nextPlayOrder;
}
