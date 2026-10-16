# Saved soccer matches and seeded reruns

Soccer saves a separate match record before loading the simulator. It checkpoints that
record every five game-time seconds, saves the result at final whistle, and saves again
at teardown. Files live at:

`Application.persistentDataPath/soccer/<channel-key>/matches/<match-id>.json`

This is a **seeded simulation rerun with recorded node playback**, not a recording of
player/ball trajectories. A rerun can diverge in action or score because gameplay still
depends on Unity frame timing, physics, and asynchronous initialization. There is no
seeking, headless simulation, or frame-exact playback in this slice.

## Starting and reusing a setup

Set `MatchSeed` in soccer configuration, or include `MatchSeed: 12345` in a soccer
chat's topic/idea metadata. The metadata takes precedence. Absent a seed, a new signed
32-bit seed is generated and saved. Invalid explicit seed metadata rejects startup.

The saved setup includes the selected team template names, complete public player
fields, formation, both kits' colors and texture identities, logo colors/identity,
home/away kit selections, AI level, time of day, roster context, engine settings JSON,
Unity version, clock settings, and RNG algorithm version. Team selection also uses the
seed. Identical seed alone does not freeze changed cast colors, team assets, or rosters;
reuse the saved setup to freeze those inputs.

On the active soccer channel, put a saved match ID in SoccerGameSource's
`Saved Match To Replay` Inspector field, leave `Replay Saved Dialogue` enabled, and
run **Replay saved soccer match** from its component context menu. The replay queues
through normal chat scene/actor preparation. Each execution receives a new match ID
and saves its own record, with `ReplayOf` identifying the original.

Runtime entry points:

```csharp
source.QueueSavedMatch(matchId);        // Seeded rerun with the recorded performance.
source.QueueSavedMatch(matchId, false); // Same setup, fresh host/reaction behavior.

var setup = source.LoadSavedMatch(matchId).Setup;
setup.Seed = 12345;
source.QueueMatchSetup(setup);          // Edited parameters, normal live dialogue.
```

Queueing requires the owning channel to be active and no match currently loading,
playing, or unloading. Referenced actors/templates must exist. Restore rejects a
different Unity version, engine settings, clock settings, unsupported schema/RNG,
ambiguous team templates, missing fields, or changed named texture dependencies.
Texture contents and other external assets are not embedded: keep the vendor assets
backed up. Changing contents under an unchanged texture name is not detected.

The team snapshots restore into independent runtime clones. The vendor's shallow kit
and logo references must not let match recoloring mutate the template assets.

## Replay score integrity

Each new rerun (with saved or fresh dialogue) snapshots the source's final scores and
completion state in `ReplayIntegrity`, alongside `ReplayOf`. The rerun's own
`HomeScore`/`AwayScore` remain its outcome; the source file is never updated.
At final whistle, `ReplayIntegrity.Result` becomes `match` or `mismatch` by comparing
both scores. This checks final scores only, not gameplay or deterministic simulation.

Before final whistle the result is `pending`. Teardown without final whistle marks
it `aborted`, even if the partial score happens to equal the source. An interrupted
process may leave a `pending` checkpoint; that is unverified, never a successful match.
A completed rerun of an incomplete source has result `unavailable`. Original matches
and older records have no `ReplayIntegrity` and carry no verification claim.

The existing `StartupStatus` Inspector field and `[Soccer]` log show the rerun/source
IDs, comparison result, original score/completion state, and rerun final score (or
explicitly no final score for an unfinished run). Normal post-whistle teardown keeps
the completed comparison. Saved-dialogue result-report suppression is unchanged.

## What the performance records

Every host-channel node activated during the recording interval is copied, including
silent actions, normal host dialogue, and temporary reaction nodes. Each entry contains
activation offset, sequence, source chat ID, match minute, and the serialized node.
Later checkpoints fill in audio produced after activation without changing the saved
text/actions. Announcer lines include their generated audio. Explicit node interruption
requests are also timestamped.

Activation means the director began presenting the node. It does not assert that its
action succeeded or its speech was heard/completed. The record retains full node text
and audio even when delivery is partial. Audio completion/provenance work can annotate
these entries separately; fully completed quotes remain a distinct report concern.

Capture starts after setup preparation, continues across host chats, and ends at match
teardown (normally ten seconds after final whistle). Later postgame chats are outside
this interval. Unactivated queued packets are omitted. Reaction nodes require no chat
save to survive. The match stores copies rather than references to runtime nodes.

Playback uses recorded activation offsets and preserves each node's own delay/actions.
Nodes dispatch independently so confidence-based coroutine duration does not shift
later cues. Saved announcer audio and explicit interruptions are dispatched as cues.
Missing saved audio remains missing; replay does not generate replacement speech.
Scene assets and action handlers remain dependencies, so action outcomes are not
guaranteed identical. Full audio stop/sample accounting and recorded game-state playback
are follow-on work. The existing global chat pause flag is not a synchronized replay
transport; use normal stop/skip to end this seeded replay.

Saved-dialogue reruns suppress fresh soccer interrupts/announcer generation, soccer
event publishing, postgame generation, result report creation, and chat memory posting.
They do not mark the original schedule event complete again. Existing chat presentation
(including configured dialogue/title publishing) still applies. Original result reports
gain optional `SavedMatchId` and `Seed` fields; historical reports remain readable.

## Workstation installation

FootballSimulator source and art remain ignored dependencies. The complete
[workstation setup guide](../Tools/FootballSimulator/README.md) now rebuilds every
captured simulator change from the pinned original Asset Store package, including all
earlier host-scene, camera, loader, singleton, teardown, and seed changes:

```powershell
.\Tools\FootballSimulator\Setup.ps1 -Package 'D:\Backups\Football Soccer Simulator.unitypackage'
.\Tools\FootballSimulator\Setup.ps1 -Verify
```

Close Unity before installation. The bundle includes materials, scenes, importer metadata,
team assets, and baked lighting as well as source changes, and preserves GUIDs. Keep the
pinned original package separately backed up. The old `apply_seed_patch.py` is only an
incremental compatibility tool and is not needed after complete setup. Patch rationale
remains in [FootballSimulatorVendorPatches.md](FootballSimulatorVendorPatches.md).

The gameplay stream is `xorshift32-v1`, reset immediately before match creation. It
does not reset or consume Unity's global random state. Different presentation timing
therefore cannot consume the gameplay stream directly, although simulation timing can
still change which gameplay decisions happen and in what order.

## Verification

Run `Tests/Soccer/Run.ps1`. It exercises the production RNG, setup persistence,
clone/snapshot/restore logic (against minimal Unity/vendor asset stubs), silent/action
nodes, saved audio/timing, original/replay isolation, interrupted-run updates, input
validation, and existing soccer roster/schedule/report behavior.

Unity smoke check still required on the target workstation:

1. Start a short seeded match. Activate a silent action and a reaction, and let an
   announcer line play. Inspect the saved setup/nodes and the result report reference.
2. Restart the application and replay the match through the component menu. Confirm
   the frozen teams/kits, saved speech/actions, and absence of new reaction generation.
3. Skip the replay and switch channels during another replay. Confirm scene/voice
   cleanup and that the original match/report/schedule record remains untouched.
4. Rerun with fresh dialogue and a changed seed; compare saved outcomes without
   expecting determinism. Test a missing actor/template and a changed engine setting.
