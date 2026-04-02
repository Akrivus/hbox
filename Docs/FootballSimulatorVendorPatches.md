# Football Simulator Vendor Patches

This project currently carries a small set of local patches against the Football Simulator asset under [`Assets/3rdParty/FootballSimulator`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator).

## Why These Exist

HBOx embeds football matches inside the live polbots scene instead of letting the asset run as its own standalone application. The vendor asset assumes ownership of scene loading and long-lived bootstrap objects, so a few targeted changes were needed to preserve the host scene and cleanly tear matches down.

## Current Vendor Patches

### [`SceneLoader.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/3rdParty/FootballSimulator/Code/Loaders/SceneLoader.cs)

- Added `SceneLoader.PreserveHostScene`.
- When enabled, `LoadDefaultScene()` and `LoadScene(...)` use additive scene loading instead of `LoadSceneMode.Single`.
- HBOx turns this on only while embedded soccer is active.

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

## HBOx Integration Hooks Depending On These Patches

### [`SoccerGameSource.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerGameSource.cs)

- Enables `SceneLoader.PreserveHostScene` while soccer is embedded.
- Disables the asset `Boot` / `DefaultSceneLoader` startup behaviors inside `_StartingScene`.
- Uses explicit football teardown instead of relying on scene unload alone.
- Claims football camera ownership during embedded matches and restores host cameras after teardown.
- Rebinds the football main camera to the polbots broadcast render texture and watches for dropped `targetTexture` assignments during runtime.
- Explicitly destroys tracked football `DontDestroyOnLoad` objects during normal match end and abrupt host-scene teardown.

## If The Vendor Asset Is Updated

Reapply these changes first:

1. embedded additive scene loading
2. additive scene unload tracking
3. tracked football `DontDestroy` teardown
4. singleton cache refresh / clear on disable-destroy
5. idempotent single-addressable prefab loading
6. `MatchManager` null-safety around late-update offside calculations

If the asset is moved to a private submodule, keep this file in the main repo so the integration contract stays documented.
