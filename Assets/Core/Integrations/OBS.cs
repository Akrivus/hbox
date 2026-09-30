using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

public class OBS : MonoBehaviour, IConfigurable<OBSConfigs>
{
    [SerializeField] private string VideosFolder;
    [SerializeField] private string OBSWebSocketURI = "ws://localhost:4455";
    [SerializeField] private bool IsStreaming;
    [SerializeField] private bool IsRecording;
    [SerializeField] private bool DoSplitRecording;
    [SerializeField] private bool OnlyNewEpisodes = true;
    private string password;
    private ChatManager manager;
    private string configuredChannelKey;
    private bool hasConfiguration;
    private bool shuttingDown;
    private readonly ObsRecordingSession recording = new ObsRecordingSession();
    private Task operations = Task.CompletedTask;
    public static string ProductionCode = null;

    public void Configure(OBSConfigs c)
    {
        var connectionChanged = OBSWebSocketURI != c.OBSWebSocketURI || password != c.OBSWebSocketPassword;
        var updateStream = !hasConfiguration || connectionChanged || IsStreaming != c.IsStreaming;
        if (connectionChanged) StopRecording();
        hasConfiguration = true;
        VideosFolder = c.VideosFolder; // Legacy setting; actual paths now come from OBS.
        OBSWebSocketURI = c.OBSWebSocketURI;
        password = c.OBSWebSocketPassword;
        IsStreaming = c.IsStreaming;
        IsRecording = c.IsRecording;
        DoSplitRecording = c.DoSplitRecording;
        OnlyNewEpisodes = c.OnlyNewEpisodes;
        if (updateStream)
        {
            if (IsStreaming) StartStreaming(); else StopStreaming();
        }
        if (!IsRecording) StopRecording();
    }

    private void Start()
    {
        manager = ChatManager.Instance;
        manager.OnContextChanged += OnContextChanged;
        manager.OnPlaybackPreparing += PrepareRecording;
        manager.OnChatQueueEmpty += StopRecording;
        if (manager.CurrentContext != null) OnContextChanged(manager.CurrentContext);
    }

    private void OnDestroy()
    {
        shuttingDown = true;
        if (manager != null)
        {
            manager.OnContextChanged -= OnContextChanged;
            manager.OnPlaybackPreparing -= PrepareRecording;
            manager.OnChatQueueEmpty -= StopRecording;
        }
        StopRecording();
        if (IsStreaming) StopStreaming();
    }

    private void OnContextChanged(ChatManagerContext context)
    {
        // ChatManager raises this even for consecutive episodes in the same channel.
        if (!string.Equals(configuredChannelKey, context.Key, StringComparison.OrdinalIgnoreCase))
        {
            StopRecording(); // The session retains its original connection until finalized.
            configuredChannelKey = context.Key;
            hasConfiguration = false;
        }
        IsRecording = false; // A channel without an OBS config must not inherit recording.
        context.ConfigManager.RegisterConfig(typeof(OBSConfigs), "obs", config => Configure((OBSConfigs)config));
    }

    private IEnumerator PrepareRecording(Chat chat)
    {
        var pending = QueueEpisode(chat);
        // Wait for OBS acknowledgement before the first dialogue is performed.
        while (!pending.IsCompleted) yield return null;
    }

    private Task QueueEpisode(Chat chat)
    {
        var episode = chat == null ? null : new RecordedEpisode
        {
            channelKey = chat.Key, slug = chat.FileName, title = chat.Title
        };
        var shouldRecord = IsRecording && chat != null && (!OnlyNewEpisodes || chat.NewEpisode);
        var split = DoSplitRecording;
        var client = CreateClient();
        return Queue(async () =>
        {
            if (!shouldRecord || shuttingDown || manager == null || manager.NowPlaying != chat)
                await recording.StopAsync();
            else
            {
                await recording.BeginAsync(episode, split, client.RequestAsync);
                if (shuttingDown || manager == null || manager.NowPlaying != chat)
                    await recording.StopAsync();
            }
        });
    }

    public void StopOrStartRecording(Chat chat) => QueueEpisode(chat);
    public void StartRecording() => QueueEpisode(ChatManager.Instance?.NowPlaying);
    public void StopRecording() => Queue(() => recording.StopAsync());
    // Legacy Unity event entry point; the next intermission starts the new file.
    public void SplitRecording() => StopRecording();
    public void StartStreaming() => SetStreaming(true);
    public void StopStreaming() => SetStreaming(false);

    private void SetStreaming(bool active)
    {
        var client = CreateClient();
        Queue(async () =>
        {
            var status = await client.RequestAsync("GetStreamStatus");
            if (status.Value<bool>("outputActive") != active)
                await client.RequestAsync(active ? "StartStream" : "StopStream");
        });
    }

    private ObsWebSocketClient CreateClient() => new ObsWebSocketClient(OBSWebSocketURI, password);

    private Task Queue(Func<Task> operation) => operations = RunAfterAsync(operations, operation);

    private static async Task RunAfterAsync(Task previous, Func<Task> operation)
    {
        await previous;
        try { await operation(); }
        catch (Exception error) { Debug.LogError($"OBS: {error.Message}"); }
    }
}