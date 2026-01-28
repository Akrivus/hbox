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

        ReplayNewEpisode();
    }

    public void ReplayNewEpisode()
    {
        StartCoroutine(ReplayEpisodes());
    }

    private IEnumerator ReplayEpisodes()
    {
        yield return FetchFiles(ReplaysPerBatch);
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(FolderConfigs), "folder", (_config) => Configure((FolderConfigs)_config));
    }

    private void OnDestroy()
    {
        ChatManagerContext.Current.OnChatQueueEmpty -= ReplayNewEpisode;
        StopAllCoroutines();
    }

    private IEnumerator FetchFiles(int count)
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
        if (unplayed.Count() > ReplayRate)
            titles = unplayed;

        var tasks = titles
            .Shuffle()
            .Take(count)
            .Select(LogThenLoad)
            .ToList();

        foreach (var task in tasks)
            yield return task.AsCoroutine();

        if (tasks.Count > 0)
            UiEventBus.Publish(ChatManagerContext.Current, $"Loaded {tasks.Count} replay{(tasks.Count == 1 ? "" : "s")}");
    }

    private async Task LogThenLoad(string title)
    {
        try
        {
            var chat = await Chat.Load(ReplayDirectory, title);
            AddReplayToList(title);
            ChatManager.Instance.AddToPlayList(chat);
        }
        catch
        {
            replays.Remove(title);
        }
    }

    private List<string> LoadReplays()
    {
        fileName = $"replays-{ChatManagerContext.Current.Key}.txt";
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
            .Shuffle()
            .TakeLast(ReplayRate)
            .ToList());
    }
}