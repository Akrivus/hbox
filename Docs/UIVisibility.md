# UI Visibility Rules

`UiVisibilityPolicy` is the single place that combines call availability, full-screen soccer, new-episode playback, and an empty playback queue. `UiVisibilityCoordinator` is installed automatically on the persistent ChatManager. It subscribes to playback events, samples current call/match state once in LateUpdate, and applies the resulting permissions to the current scene's widgets. No Inspector wiring is required.

| Situation | Bottom call overlay | Episode title | Top left event feed |
| --- | --- | --- | --- |
| Normal replay | Allowed when call UI is enabled | Existing title timing applies | Visible |
| New episode | Allowed when call UI is enabled | Existing title timing applies | Fades out |
| Empty playback queue | Allowed when call UI is enabled | Existing title timing applies | Fades in |
| Globe | Hidden because globe disables call UI | Existing title timing applies | Existing episode/queue rule applies |
| Full screen soccer | Hidden | Suppressed | Fades out, including during replay or empty queue |

Ending one condition does not override another. Soccer ending during a new episode keeps the feed hidden. Soccer ending into globe keeps the call overlay hidden. Title suppression releases to its existing timed animation state rather than restarting its duration.

Mode code reports its existing behavior: GlobeController enables/disables call availability and SoccerGameSource exposes the live full-screen match state. Neither sets the footer's visibility. UiEventFeed, SubtitleManager, and VideoCallUIManager no longer inspect soccer individually or combine visibility rules. Their rendering methods apply the central result and retain their own fade/content mechanics; feed transitions cancel an older root fade before starting another.

Add future visibility conditions to the coordinator and policy, rather than adding more mode checks inside widgets. Animation durations, title text/splash timing, toast lifetime, head layout, and subtitles remain in their existing components. This is a UI policy, not a portable channel-mode contract.

Verification: `Tests/UIVisibility/Run.ps1` exercises eight normal and overlapping-condition scenarios. Assembly-CSharp source compiles against installed Unity/package references. Confirm the transitions visually in Unity: call to globe, replay to a new episode, soccer entry, soccer exit during a new episode, and soccer exit into globe.
