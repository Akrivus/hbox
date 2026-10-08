using UnityEngine;

// Installed on the persistent ChatManager; no scene or prefab references need wiring.
[DisallowMultipleComponent]
public sealed class UiVisibilityCoordinator : MonoBehaviour
{
    private ChatManager manager;
    private bool newEpisode;
    private bool queueEmpty = true;

    private void OnEnable()
    {
        manager = GetComponent<ChatManager>();
        if (manager == null) return;
        manager.OnChatLoaded += OnChatLoaded;
        manager.OnChatQueueEmpty += OnQueueEmpty;
        if (manager.NowPlaying != null) OnChatLoaded(manager.NowPlaying);
    }

    private void OnDisable()
    {
        if (manager == null) return;
        manager.OnChatLoaded -= OnChatLoaded;
        manager.OnChatQueueEmpty -= OnQueueEmpty;
    }

    private void OnChatLoaded(Chat chat)
    {
        newEpisode = chat != null && chat.NewEpisode;
        queueEmpty = false;
    }

    private void OnQueueEmpty() => queueEmpty = true;

    private void LateUpdate()
    {
        // Sample once after mode changes. Applying every frame also reaches recreated scene widgets.
        var call = VideoCallUIManager.Instance;
        var state = UiVisibilityPolicy.Resolve(call != null && call.Enabled,
            SoccerGameSource.Instance != null && SoccerGameSource.Instance.IsFullScreenMatch,
            newEpisode, queueEmpty);
        if (call != null) call.SetCallOverlayVisible(state.CallOverlayVisible);
        if (SubtitleManager.Instance != null) SubtitleManager.Instance.SetEpisodeTitleAllowed(state.EpisodeTitleAllowed);
        if (UiEventFeed.Instance != null) UiEventFeed.Instance.SetFeedVisible(state.EventFeedVisible);
    }
}
