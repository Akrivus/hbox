using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class FolderSource : MonoBehaviour, IConfigurable<FolderConfigs>
{
    public string ReplayDirectory;
    public int ReplayRate = 80;
    public int ReplaysPerBatch = 20;
    public int MaxReplayAgeInMinutes = 1440;

    private List<string> replays = new List<string>();
    private ConcurrentQueue<Lazy<Task<Chat>>> queue = new ConcurrentQueue<Lazy<Task<Chat>>>();
    private string fileName;

    public void Configure(FolderConfigs c)
    {
        ReplayDirectory = c.ReplayDirectory;
        ReplayRate = c.ReplayRate;
        ReplaysPerBatch = c.ReplaysPerBatch;
        MaxReplayAgeInMinutes = c.MaxReplayAgeInMinutes;

        if (MaxReplayAgeInMinutes < 1)
            MaxReplayAgeInMinutes = 1440 * 365;

        replays = LoadReplays();

        ChatManagerContext.Current.OnChatQueueEmpty += ReplayNewEpisode;
        ChatManagerContext.Current.OnChatLoaded += AddReplayToList;

        ReplayNewEpisode();
    }

    public void AddReplayToList(Chat chat)
    {
        if (replays.Contains(chat.FileName))
            return;
        replays.Add(chat.FileName);
    }

    public void ReplayNewEpisode()
    {
        StartCoroutine(ReplayEpisodes());
    }

    private IEnumerator ReplayEpisodes()
    {
        FetchFiles(ReplaysPerBatch);

        while (queue.TryDequeue(out var task))
            yield return ReplayEpisode(task).AsCoroutine();
    }

    private async Task ReplayEpisode(Lazy<Task<Chat>> lazyTask)
    {
        var task = lazyTask.Value;
        var chat = await task;

        ChatManager.Instance.AddToPlayList(chat);
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(FolderConfigs), "folder", (_config) => Configure((FolderConfigs)_config));
    }

    private void OnDestroy()
    {
        ChatManagerContext.Current.OnChatQueueEmpty -= ReplayNewEpisode;
        ChatManagerContext.Current.OnChatLoaded -= AddReplayToList;
        StopAllCoroutines();
        File.WriteAllLines(fileName, replays);
    }

    private void FetchFiles(int count)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, ReplayDirectory);

        var titles = Directory.GetFiles(path, "*.json")
            .Where(file => File.GetLastWriteTime(file) > DateTime.Now.AddMinutes(-MaxReplayAgeInMinutes));
        if (titles.Count() == 0)
            titles = Directory.GetFiles(path, "*.json");
        titles = titles
            .OrderBy(file => File.GetLastWriteTime(file))
            .Reverse() // newest first
            .Select(Path.GetFileNameWithoutExtension);
        var unplayed = titles.Except(replays);
        if (unplayed.Any())
            titles = unplayed;

        var tasks = titles
            .Take(count)
            .Reverse() // load oldest first
            .Select(LogThenLoad)
            .ToList();

        foreach (var task in tasks)
            queue.Enqueue(task);

        if (tasks.Count > 0)
            UiEventBus.Publish(ChatManagerContext.Current, $"Loaded {tasks.Count} replay{(tasks.Count == 1 ? "" : "s")}");
    }

    private Lazy<Task<Chat>> LogThenLoad(string title)
    {
        replays = replays.TakeLast(ReplayRate - 1).ToList();
        replays.Add(title);
        return new Lazy<Task<Chat>>(() => Chat.Load(ReplayDirectory, title));
    }

    private List<string> LoadReplays()
    {
        fileName = $"replays-{ChatManagerContext.Current.Key}.txt";
        if (!File.Exists(fileName))
            return new List<string>();
        return File.ReadAllLines(fileName)
            .ToList();
    }
}