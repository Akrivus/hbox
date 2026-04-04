using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

public sealed class SoccerAnnouncerService
{
    private const int MaxCachedClips = 24;

    public int QueueCount => queue.Count;
    public int ClipCacheCount => clipCache.Count;
    public bool MatchActive => matchActive;

    private readonly TextToSpeechGenerator ttsGenerator;
    private readonly Queue<string> queue = new Queue<string>();
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

    private AudioSource audioSource;
    private string voice = "alloy";
    private int maxQueue = 4;
    private bool enabled = true;
    private bool skipDuringInterrupts;
    private int generationVersion;
    private int pendingGeneration;
    private Task<AudioClip> pendingClipTask;
    private bool matchActive;

    public SoccerAnnouncerService(TextToSpeechGenerator ttsGenerator)
    {
        this.ttsGenerator = ttsGenerator;
    }

    public void Configure(SoccerConfigs config, AudioSource audioSource)
    {
        this.audioSource = audioSource;
        enabled = config?.EnableAnnouncer ?? true;
        maxQueue = Mathf.Max(1, config?.MaxAnnouncerQueue ?? 4);
        skipDuringInterrupts = config?.SkipAnnouncerDuringInterrupts ?? false;
        voice = string.IsNullOrWhiteSpace(config?.AnnouncerVoice) ? "alloy" : config.AnnouncerVoice;

        if (this.audioSource != null)
            this.audioSource.volume = Mathf.Clamp01(config?.AnnouncerVolume ?? 1f);
    }

    public void BeginMatch()
    {
        generationVersion++;
        matchActive = true;
        queue.Clear();
        clipCache.Clear();
        pendingClipTask = null;

        if (audioSource != null)
            audioSource.Stop();
    }

    public void EndMatch()
    {
        generationVersion++;
        matchActive = false;
        queue.Clear();
        clipCache.Clear();
        pendingClipTask = null;

        if (audioSource != null)
            audioSource.Stop();
    }

    public void EnqueueLine(string line)
    {
        if (!matchActive || !enabled)
            return;

        var scrubbed = Scrub(line);
        if (string.IsNullOrWhiteSpace(scrubbed))
            return;

        while (queue.Count >= maxQueue)
            queue.Dequeue();

        queue.Enqueue(scrubbed);
    }

    public void Tick(bool interruptActive)
    {
        if (!matchActive || !enabled || audioSource == null)
            return;

        if (skipDuringInterrupts && interruptActive)
            return;

        if (audioSource.isPlaying)
            return;

        if (pendingClipTask != null)
        {
            if (!pendingClipTask.IsCompleted)
                return;

            var clip = pendingClipTask.Status == TaskStatus.RanToCompletion ? pendingClipTask.Result : null;
            var generation = pendingGeneration;
            pendingClipTask = null;

            if (generation != generationVersion || !matchActive || clip == null)
                return;

            audioSource.clip = clip;
            audioSource.Play();
            return;
        }

        if (queue.Count == 0)
            return;

        var line = queue.Dequeue();
        pendingGeneration = generationVersion;
        pendingClipTask = GetClip(line, pendingGeneration);
    }

    private async Task<AudioClip> GetClip(string line, int generation)
    {
        if (generation != generationVersion || string.IsNullOrWhiteSpace(line))
            return null;

        if (clipCache.TryGetValue(line, out var cached) && cached != null)
            return cached;

        if (ttsGenerator == null)
            return null;

        var actor = ScriptableObject.CreateInstance<Actor>();
        actor.Name = "Announcer";
        actor.Aliases = new[] { "Announcer" };
        actor.Voice = voice;
        actor.Confidence = 1f;

        var node = new ChatNode(actor, line);
        await ttsGenerator.GenerateTextToSpeech(node);

        if (generation != generationVersion)
            return null;

        var clip = node.AudioClip;
        if (clip != null)
        {
            while (clipCache.Count >= MaxCachedClips)
            {
                var oldestKey = clipCache.Keys.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(oldestKey))
                    break;
                clipCache.Remove(oldestKey);
            }
            clipCache[line] = clip;
        }

        return clip;
    }

    private static string Scrub(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        var text = line
            .Replace("#", string.Empty)
            .Replace("**", string.Empty)
            .Replace(":soccer:", string.Empty)
            .Replace("—", ", ");

        text = Regex.Replace(text, "[(<].*?[>)]", string.Empty);
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text;
    }
}
