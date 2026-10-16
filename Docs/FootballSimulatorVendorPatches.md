# Football Simulator Vendor Patches

## Complete workstation setup

All current local simulator differences are now captured against the original licensed
package in the [complete patch bundle](../Tools/FootballSimulator/README.md). From the
HBOx repository root, with Unity closed:

```powershell
.\Tools\FootballSimulator\Setup.ps1 -Package 'D:\Backups\Football Soccer Simulator.unitypackage'
.\Tools\FootballSimulator\Setup.ps1 -Verify
```

This restores the entire vendor subtree, preserving GUIDs, from an empty directory or
recognized existing baseline. It includes the historical patches below, Unity API/input
upgrades, seed changes, materials/scenes/importer changes, and added database/lighting
assets. The pinned package hash, full inventory, rebuild commands, and verification
instructions live in the linked guide. Package ProjectSettings do not overwrite HBOx.

The original notes below explain intent; the generated bundle manifest records the
complete current byte-level difference, including changes these notes did not list.

This project carries local changes against the Football Simulator asset under [`Assets/3rdParty/FootballSimulator`](../Assets/3rdParty/FootballSimulator).

## Why These Exist

HBOx embeds football matches inside the live polbots scene instead of letting the asset run as its own standalone application. The vendor asset assumes ownership of scene loading and long-lived bootstrap objects, so a few targeted changes were needed to preserve the host scene and cleanly tear matches down.

## Current Vendor Patches

### [`SceneLoader.cs`](../Assets/3rdParty/FootballSimulator/Code/Loaders/SceneLoader.cs)

- Added `SceneLoader.PreserveHostScene`.
- Added `SceneLoader.PushPreserveHostScene()` / `PopPreserveHostScene()` so embedded hosts can pin preserve mode across async vendor startup.
- When enabled, `LoadDefaultScene()` and `LoadScene(...)` use additive scene loading instead of `LoadSceneMode.Single`.
- Scene loading also falls back to additive mode whenever more than one Unity scene is already loaded.
- This protects embedded HBOx runs even if the explicit preserve flag is dropped during vendor startup or teardown ordering.
- HBOx turns this on only while embedded soccer is active.

### [`Boot.cs`](../Assets/3rdParty/FootballSimulator/Code/Boot.cs)

- `Start()` exits early while `SceneLoader.PreserveHostScene` is enabled.
- This keeps the vendor standalone bootstrap from taking over when HBOx embeds soccer inside the existing polbots scene.

### [`DefaultSceneLoader.cs`](../Assets/3rdParty/FootballSimulator/Code/Loaders/DefaultSceneLoader.cs)

- `Start()` exits early while `SceneLoader.PreserveHostScene` is enabled.
- This prevents the vendor default-scene bootstrap path from unloading or replacing the host scene during embedded startup.

### [`SerializableSceneCollection.cs`](../Assets/3rdParty/FootballSimulator/Code/Utilities/SerializableSceneCollection.cs)

- Tracks the last loaded addressable scene instance.
- When `SceneLoader.PreserveHostScene` is enabled, `Unload()` unloads the tracked additive scene instance directly instead of loading the default scene.
- This prevents match teardown from overwriting the host polbots scene.
- `Load(...)` now unloads an already tracked scene instance before loading a new one.
- This hardens the shared stadium loader against duplicate additive stadium loads if a second match start slips through.

### [`DontDestroy.cs`](../Assets/3rdParty/FootballSimulator/Code/UI/Utilities/DontDestroy.cs)

- Added tracked destroy groups for vendor `DontDestroyOnLoad` objects.
- Objects register into the `"football"` group by default.
- HBOx can now explicitly destroy tracked football singletons on match teardown instead of leaving them resident across runs.

### [`SceneObjectSingleton.cs`](../Assets/3rdParty/FootballSimulator/Code/Utilities/SceneObjectSingleton.cs)

- The singleton cache now refreshes on `OnEnable()`.
- The cached current instance clears on `OnDisable()` and `OnDestroy()`.
- This prevents stale singleton references from surviving scene teardown and pointing later match logic at dead cameras, loaders, or managers.

### [`SingleAddressableLoader.cs`](../Assets/3rdParty/FootballSimulator/Code/Loaders/SingleAddressableLoader.cs)

- `Load()` now no-ops if its prefab is already instantiated.
- This prevents repeated match UI or prefab instantiation when an embedded launch path is invoked more than once.

### [`MatchManager.cs`](../Assets/3rdParty/FootballSimulator/Code/MatchEngine/MatchManager.cs)

- Added a live-reference guard at the top of `LateUpdate()`.
- Added null/length guards inside `CalculateOffsideLine(...)`.
- This avoids per-frame null exceptions when match state is in transition and keeps the sim from hard-crashing while lifecycle issues are being cleaned up.

### [`MatchEngineLoader.cs`](../Assets/3rdParty/FootballSimulator/Code/MatchEngine/MatchEngineLoader.cs)

- Skips match-camera `cullingMask` and `clearFlags` mutation while `SceneLoader.PreserveHostScene` is enabled.
- This prevents embedded match startup from visually taking over the host polbots camera.

### [`CameraTransition.cs`](../Assets/3rdParty/FootballSimulator/Code/CameraTransition/CameraTransition.cs)

- Uses the Football Simulator `MainCamera` singleton instead of Unity `Camera.main` when capturing transition frames.
- This prevents transition effects from briefly stealing or rendering through the host polbots camera.

### Seeded gameplay RNG (saved matches)

- Gameplay random ranges and GUID-based player shuffles in 21 vendor files now use
  the tracked `FStudio.MatchEngine.SoccerSimulationRandom` stream.
- `MatchManager` exposes `SoccerSeedPatchVersion = 1`; HBOx resets the gameplay stream
  from the saved match seed before match creation.
- Presentation randomness remains separate. This improves repeatability without
  establishing deterministic physics or frame timing.
- The exact affected files and before/after hashes are tracked in
  [`seed-patch.json`](../Tools/FootballSimulator/seed-patch.json). Apply/verify with
  `python Tools/FootballSimulator/apply_seed_patch.py`.
- **This script is an incremental seed patch; use the complete setup above on a new workstation.** Its
  input hashes were captured from the already modified local package, including earlier
  changes documented here. A pristine vendor package may fail verification until those
  changes have been restored. The script does not reconstruct them.
- Saved-match behavior and verification are documented in
  [`SoccerSavedMatches.md`](SoccerSavedMatches.md).

## Conditional Vendor Patches

These patches were conditional during embedded startup debugging. The complete bundle
includes their current working implementations together. The following rationale is
retained for future vendor upgrades; it is not an instruction to remove them from a
reconstructed workstation.

### [`MatchEngineLoader.cs`](../Assets/3rdParty/FootballSimulator/Code/MatchEngine/MatchEngineLoader.cs)

- Waits briefly for `ShadowRenderer` and `FootShadowRenderer` to exist and initialize after stadium scene loading.
- This was added after player creation hit `ShadowRenderer.Current.Get()` / `FootShadowRenderer.Current.Get()` null references.
- Later testing showed dry-run startup/destruction could also trigger these nulls, so this patch is defensive rather than a confirmed first-pass requirement.

### [`AbstractEventRenderer.cs`](../Assets/3rdParty/FootballSimulator/Code/MatchEngine/Graphics/EventRenderer/AbstractEventRenderer.cs)

- Exposes `IsReady` once the renderer pool has been initialized.
- `MatchEngineLoader` uses this for the conditional event-renderer startup wait above.

## HBOx Integration Hooks Depending On These Patches

### [`SoccerGameSource.cs`](../Assets/Scenes/polbots/Scripts/Integrations/SoccerGameSource.cs)

- Enables `SceneLoader.PreserveHostScene` while soccer is embedded.
- Disables the asset `Boot` / `DefaultSceneLoader` startup behaviors inside `_StartingScene`; the vendor files also contain preserve-mode early returns as a race guard.
- Uses explicit football teardown instead of relying on scene unload alone.
- Leaves the host `MainCamera` ownership intact during embedded matches.
- Rebinds the football camera to the polbots broadcast render texture and watches for dropped `targetTexture` assignments during runtime.
- Explicitly destroys tracked football `DontDestroyOnLoad` objects during normal match end and abrupt host-scene teardown.
- Treats only the configured soccer game scene unload as soccer teardown; unrelated additive scene unloads must not disable `PreserveHostScene`.
- Defers `GameOnStart` until the polbots context is live and `ChatManager.ReadyForAction` is true; this prevents dry-run/context-population passes from starting and then destroying a real match.

## If The Vendor Asset Is Updated

For a vendor upgrade, review these integration requirements first:

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

For a different vendor release, capture and review a new complete bundle against that
exact original package. Existing manifests intentionally reject a changed distribution.
Do not replace expected hashes merely to bypass verification.

All these changes are now included in the complete automated bundle. Preserve the
pinned original distribution and the tracked bundle. A backup of the working modified
package remains useful, but is no longer required to reconstruct the captured tree.

If the asset is moved to a private submodule, keep this file in the main repo so the integration contract stays documented.
