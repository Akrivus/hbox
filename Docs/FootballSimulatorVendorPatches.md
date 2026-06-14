# Football Simulator Vendor Patches

This project currently carries a small set of local patches against the Football Simulator asset under [`Assets/3rdParty/FootballSimulator`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator).

## Why These Exist

HBOx embeds football matches inside the live polbots scene instead of letting the asset run as its own standalone application. The vendor asset assumes ownership of scene loading and long-lived bootstrap objects, so a few targeted changes were needed to preserve the host scene and cleanly tear matches down.

## Current Vendor Patches

### [`SceneLoader.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Loaders/SceneLoader.cs)

- Added `SceneLoader.PreserveHostScene`.
- Added `SceneLoader.PushPreserveHostScene()` / `PopPreserveHostScene()` so embedded hosts can pin preserve mode across async vendor startup.
- When enabled, `LoadDefaultScene()` and `LoadScene(...)` use additive scene loading instead of `LoadSceneMode.Single`.
- Scene loading also falls back to additive mode whenever more than one Unity scene is already loaded.
- This protects embedded HBOx runs even if the explicit preserve flag is dropped during vendor startup or teardown ordering.
- HBOx turns this on only while embedded soccer is active.

### [`Boot.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Boot.cs)

- `Start()` exits early while `SceneLoader.PreserveHostScene` is enabled.
- This keeps the vendor standalone bootstrap from taking over when HBOx embeds soccer inside the existing polbots scene.

### [`DefaultSceneLoader.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Loaders/DefaultSceneLoader.cs)

- `Start()` exits early while `SceneLoader.PreserveHostScene` is enabled.
- This prevents the vendor default-scene bootstrap path from unloading or replacing the host scene during embedded startup.

### [`SerializableSceneCollection.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Utilities/SerializableSceneCollection.cs)

- Tracks the last loaded addressable scene instance.
- When `SceneLoader.PreserveHostScene` is enabled, `Unload()` unloads the tracked additive scene instance directly instead of loading the default scene.
- This prevents match teardown from overwriting the host polbots scene.
- `Load(...)` now unloads an already tracked scene instance before loading a new one.
- This hardens the shared stadium loader against duplicate additive stadium loads if a second match start slips through.

### [`DontDestroy.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/UI/Utilities/DontDestroy.cs)

- Added tracked destroy groups for vendor `DontDestroyOnLoad` objects.
- Objects register into the `"football"` group by default.
- HBOx can now explicitly destroy tracked football singletons on match teardown instead of leaving them resident across runs.

### [`SceneObjectSingleton.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Utilities/SceneObjectSingleton.cs)

- The singleton cache now refreshes on `OnEnable()`.
- The cached current instance clears on `OnDisable()` and `OnDestroy()`.
- This prevents stale singleton references from surviving scene teardown and pointing later match logic at dead cameras, loaders, or managers.

### [`SingleAddressableLoader.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Loaders/SingleAddressableLoader.cs)

- `Load()` now no-ops if its prefab is already instantiated.
- This prevents repeated match UI or prefab instantiation when an embedded launch path is invoked more than once.

### [`MatchManager.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/MatchEngine/MatchManager.cs)

- Added a live-reference guard at the top of `LateUpdate()`.
- Added null/length guards inside `CalculateOffsideLine(...)`.
- This avoids per-frame null exceptions when match state is in transition and keeps the sim from hard-crashing while lifecycle issues are being cleaned up.

### [`MatchEngineLoader.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/MatchEngine/MatchEngineLoader.cs)

- Skips match-camera `cullingMask` and `clearFlags` mutation while `SceneLoader.PreserveHostScene` is enabled.
- This prevents embedded match startup from visually taking over the host polbots camera.

### [`CameraTransition.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/CameraTransition/CameraTransition.cs)

- Uses the Football Simulator `MainCamera` singleton instead of Unity `Camera.main` when capturing transition frames.
- This prevents transition effects from briefly stealing or rendering through the host polbots camera.

## Conditional Vendor Patches

These patches have been useful during embedded startup debugging, but they should be treated as conditional when applying a fresh vendor package. Reapply them only if normal, non-dry-run soccer startup still hits renderer-pool null references after the HBOx `GameOnStart` readiness gate is in place.

### [`MatchEngineLoader.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/MatchEngine/MatchEngineLoader.cs)

- Waits briefly for `ShadowRenderer` and `FootShadowRenderer` to exist and initialize after stadium scene loading.
- This was added after player creation hit `ShadowRenderer.Current.Get()` / `FootShadowRenderer.Current.Get()` null references.
- Later testing showed dry-run startup/destruction could also trigger these nulls, so this patch is defensive rather than a confirmed first-pass requirement.

### [`AbstractEventRenderer.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/MatchEngine/Graphics/EventRenderer/AbstractEventRenderer.cs)

- Exposes `IsReady` once the renderer pool has been initialized.
- `MatchEngineLoader` uses this for the conditional event-renderer startup wait above.

## HBOx Integration Hooks Depending On These Patches

### [`SoccerGameSource.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerGameSource.cs)

- Enables `SceneLoader.PreserveHostScene` while soccer is embedded.
- Disables the asset `Boot` / `DefaultSceneLoader` startup behaviors inside `_StartingScene`; the vendor files also contain preserve-mode early returns as a race guard.
- Uses explicit football teardown instead of relying on scene unload alone.
- Leaves the host `MainCamera` ownership intact during embedded matches.
- Rebinds the football camera to the polbots broadcast render texture and watches for dropped `targetTexture` assignments during runtime.
- Explicitly destroys tracked football `DontDestroyOnLoad` objects during normal match end and abrupt host-scene teardown.
- Treats only the configured soccer game scene unload as soccer teardown; unrelated additive scene unloads must not disable `PreserveHostScene`.
- Defers `GameOnStart` until the polbots context is live and `ChatManager.ReadyForAction` is true; this prevents dry-run/context-population passes from starting and then destroying a real match.

## If The Vendor Asset Is Updated

Reapply these changes first:

1. embedded additive scene loading
2. preserve-mode lock API in `SceneLoader`
3. additive fallback when multiple scenes are already loaded
4. preserve-mode early returns in `Boot` and `DefaultSceneLoader`
5. embedded camera mutation guards in `MatchEngineLoader` and `CameraTransition`
6. additive scene unload tracking
7. tracked football `DontDestroy` teardown
8. singleton cache refresh / clear on disable-destroy
9. idempotent single-addressable prefab loading
10. `MatchManager` null-safety around late-update offside calculations

Then run a normal embedded soccer startup. Only reapply the conditional event-renderer readiness wait if player creation still hits `ShadowRenderer` or `FootShadowRenderer` nulls outside dry-run teardown.

If the asset is moved to a private submodule, keep this file in the main repo so the integration contract stays documented.
