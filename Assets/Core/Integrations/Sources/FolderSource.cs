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
    private ReplayManifest manifest = new ReplayManifest();
    private readonly HashSet<string> knownReplayFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool isSweeping;

    public void Configure(FolderConfigs c)
    {
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
        StartCoroutine(SweepForIncomingEpisodes());

        ReplayNewEpisode();
    }

    public void ReplayNewEpisode()
    {
        StartCoroutine(ReplayEpisodes());
    }

    private IEnumerator ReplayEpisodes()
    {
        yield return new WaitUntil(() => ChatManager.Instance.ReadyForAction);
        yield return FetchFiles(ReplaysPerBatch).AsCoroutine();
    }

    private void Start()
    {
        boundContext = ChatManagerContext.Current;
        if (boundContext == null)
            return;

        boundContext.ConfigManager.RegisterConfig(typeof(FolderConfigs), "folder", (_config) => Configure((FolderConfigs)_config));
    }

    private void OnDestroy()
    {
        UnsubscribeFromQueueEmpty();
        UnsubscribeFromRuntimeEvents();
        StopAllCoroutines();
    }

    private async Task FetchFiles(int count)
    {
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
            var recentHistory = replays.Distinct().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ranked = RankReplayCandidates(allEntries).ToList();
            var unplayed = ranked
                .Where(entry => !recentHistory.Contains(entry.slug))
                .ToList();
            if (unplayed.Count < ReplayRate)
                unplayed = ranked;
            var selected = unplayed
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

        var autoQueued = 0;
        foreach (var slug in discovered.Take(ReplaysPerBatch))
        {
            if (await LogThenLoad(slug))
                autoQueued++;
        }

        if (autoQueued > 0 && boundContext != null)
            UiEventBus.Publish(boundContext, $"Detected {autoQueued} incoming episode{(autoQueued == 1 ? "" : "s")}");
    }

    private async Task<bool> LogThenLoad(string title, int attempts = 0)
    {
        if (attempts > 3) return false;
        try
        {
            var chat = await Chat.Load(ReplayDirectory, title);
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

    private void SubscribeToRuntimeEvents()
    {
        if (boundContext == null)
            return;

        boundContext.OnChatQueueAdded += OnChatQueued;
        boundContext.OnChatLoaded += OnChatLoaded;
    }

    private void UnsubscribeFromRuntimeEvents()
    {
        if (boundContext == null)
            return;

        boundContext.OnChatQueueAdded -= OnChatQueued;
        boundContext.OnChatLoaded -= OnChatLoaded;
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
            entry.lastSeenFile = GetChatPath(chat.FileName);
            if (string.IsNullOrWhiteSpace(entry.generatedAt))
                entry.generatedAt = now.ToString("O");
        });
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
        manifestPath = Path.Combine(GetReplayDirectoryPath(), ".hbox-replays.json");

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
            .ThenBy(e => ParseTimestamp(e.replayEligibleAt))
            .ThenBy(e => e.timesReplayed)
            .ThenBy(e => ParseTimestamp(e.lastPlayedAt))
            .ThenByDescending(e => ParseTimestamp(e.generatedAt))
            .ToList();

        return ranked;
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

        var snapshot = manifest.entries
            .OrderBy(e => ParseTimestamp(e.replayEligibleAt))
            .ThenBy(e => e.timesReplayed)
            .Select(e => new ReplayStatusRecord
            {
                slug = e.slug,
                title = e.title,
                channelKey = boundContext.Key,
                context = boundContext.Name,
                generatedAt = e.generatedAt,
                lastPlayedAt = e.lastPlayedAt,
                replayEligibleAt = e.replayEligibleAt,
                timesReplayed = e.timesReplayed,
                eligibleNow = ParseTimestamp(e.replayEligibleAt) <= DateTimeOffset.Now
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

    public static IReadOnlyList<ReplayStatusRecord> GetReplayStatus(string channelKey = null, int limit = 50)
    {
        lock (replaySnapshotLock)
        {
            IEnumerable<ReplayStatusRecord> rows = replaySnapshots.Values.SelectMany(v => v);
            if (!string.IsNullOrWhiteSpace(channelKey))
                rows = rows.Where(r => string.Equals(r.channelKey, channelKey, StringComparison.OrdinalIgnoreCase));

            return rows
                .OrderBy(r => r.eligibleNow ? 0 : 1)
                .ThenBy(r => ParseTimestamp(r.replayEligibleAt))
                .Take(Mathf.Clamp(limit, 1, 200))
                .Select(r => new ReplayStatusRecord
                {
                    slug = r.slug,
                    title = r.title,
                    channelKey = r.channelKey,
                    context = r.context,
                    generatedAt = r.generatedAt,
                    lastPlayedAt = r.lastPlayedAt,
                    replayEligibleAt = r.replayEligibleAt,
                    timesReplayed = r.timesReplayed,
                    eligibleNow = r.eligibleNow
                })
                .ToList();
        }
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
    public bool eligibleNow;
}
