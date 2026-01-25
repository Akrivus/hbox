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
    private ConcurrentQueue<Chat> queue = new ConcurrentQueue<Chat>();
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
    }

    public void AddReplayToList(Chat chat)
    {
        replays.Add(chat.FileName);
    }

    public void ReplayNewEpisode()
    {
        StartCoroutine(ReplayEpisodes());
    }

    private IEnumerator ReplayEpisodes()
    {
        yield return FetchFiles(ReplaysPerBatch).AsCoroutine();

        if (queue.TryDequeue(out var chat))
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

    private async Task FetchFiles(int count)
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
        if (titles.Count() >= ReplayRate)
            titles = titles.Where(title => !replays.Contains(title));
        var tasks = titles
            .Take(count)
            .Reverse() // load oldest first
            .Select(LogThenLoad)
            .ToList();

        foreach (var task in tasks)
            queue.Enqueue(await task);

        if (tasks.Count > 0)
            UiEventBus.Publish(ChatManagerContext.Current, $"Loaded {tasks.Count} replay{(tasks.Count == 1 ? "" : "s")}");
    }

    private Task<Chat> LogThenLoad(string title)
    {
        replays = replays.TakeLast(ReplayRate - 1).ToList();
        replays.Add(title);
        return Chat.Load(ReplayDirectory, title);
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