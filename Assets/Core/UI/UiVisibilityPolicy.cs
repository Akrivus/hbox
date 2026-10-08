// Visibility rules live here; widgets retain their existing animation and content behavior.
public readonly struct UiVisibilityState
{
    public bool CallOverlayVisible { get; }
    public bool EpisodeTitleAllowed { get; }
    public bool EventFeedVisible { get; }

    public UiVisibilityState(bool callOverlayVisible, bool episodeTitleAllowed, bool eventFeedVisible)
    {
        CallOverlayVisible = callOverlayVisible;
        EpisodeTitleAllowed = episodeTitleAllowed;
        EventFeedVisible = eventFeedVisible;
    }
}

public static class UiVisibilityPolicy
{
    public static UiVisibilityState Resolve(bool callEnabled, bool fullScreenMatch, bool newEpisode, bool queueEmpty)
    {
        return new UiVisibilityState(
            callEnabled && !fullScreenMatch,
            !fullScreenMatch,
            !fullScreenMatch && (queueEmpty || !newEpisode));
    }
}
