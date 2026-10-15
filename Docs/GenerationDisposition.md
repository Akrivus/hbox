# Generation disposition

`ChatGenerator` has a **Queue For Playback** checkbox directly beside **Save**. It defaults to checked, preserving existing scenes/prefabs and generators. Uncheck it to produce episodes for later replay without adding them to the playback queue. Save-only always saves, even if Save is unchecked; its tooltip states this. With Queue For Playback checked, Save retains its existing meaning, including unsaved foreground playback.

Callers can explicitly select disposition without changing Inspector settings:

```csharp
var chat = await generator.GenerateEpisode(idea, GenerationDisposition.SaveOnly);
if (chat == null)
    return; // Generation or persistence failed; no playback was queued.
```

`AddIdeaToQueue(idea, disposition)` and `AddPromptToQueue(prompt, disposition)` snapshot disposition with each queued request. Overloads without disposition, Receive, and the existing GenerateAndPlay entry point use the Inspector default. The PBNN **Queue Fixture Bulletin** menu explicitly requests SaveAndPlay, preserving its manual queue action even when the generator is configured for background production. This adds no background host, timer, channel-loading behavior or overlap prevention; a caller needs a live generator and its context.

## Persistence and replay

`Chat.Save()` now returns a Task. Generation awaits it before announcing success or queueing foreground playback. It writes complete JSON to a unique same-directory `.tmp` file and publishes through File.Move (new episode) or File.Replace (existing episode). The existing three-attempt I/O retry budget remains; the last failure propagates. Unlocked episodes fail explicitly. Staging cleanup does not mask a persistence failure. Unsupported replacement fails without deleting the old destination. Completion establishes completed publication, not power-loss durability or cross-process coordination.

Episode JSON carries `DeferAutomaticPlayback`. Missing/false preserves incoming-file autoqueueing for historical and foreground episodes. True prevents only the periodic incoming-file sweep from queueing the episode. The sweep still bootstraps the replay catalog and publishes eligibility; ordinary queue-empty replay selection and channel-return replay can select the episode. Save-only does not pause normal replay policy or forbid selecting the new episode when the channel needs another replay. It cannot withdraw playback already queued before this generation request.

The durable flag works when no folder source was active during production and after source restart/reconfiguration. Staging files never match `*.json`. Before replay handoff, FolderSource rechecks the active context and rejects JSON with a foreign channel key. No generator save-only path calls AddToPlayList or switches scenes.

This is an independent extension based on main. PR #3 (`codex/pbnn-n3-replay-rotation`) owns replay rotation/history reliability, not Chat.Save. Ranking and history methods are unchanged here; retain this sweep/queue guard when integrating PR #3. Replay manifest write behavior is unchanged. No daily scheduling, source ingestion or newsroom rendering is included.

## Deterministic regression harness

`Tests/Generation` compiles production ChatGenerator, Chat, Idea and FolderSource/config code. Only Unity, LLM/memory/API services and the playback sink are stubbed. Run from the repository root:

```sh
dotnet run --project Tests/Generation/GenerationTests.csproj -p:NewtonsoftPath=/absolute/path/to/Newtonsoft.Json.dll
```

On Windows, `Tests/Generation/Run.ps1` locates Unity's Newtonsoft package. The harness requires .NET 10 and does not change Unity's runtime target. All 24 checks pass using SDK 10.0.402 with its bundled Newtonsoft DLL. Coverage includes default foreground publication/queueing, Inspector and explicit dispositions, unsaved foreground compatibility, queued request snapshots, save-only discovery while active/inactive, later replay and source reconstruction, foreign-channel isolation, legacy JSON, batch behavior, generation/prompt/publication failures, pending generation, successful replacement/staging cleanup, ignored incomplete staging files, unlocked saves and invalid dispositions. Assertions use actual JSON and production save/load/sweep/selection methods. No paid generation or large bulletin fixture is required.

## Remaining Unity Editor checks

1. Import under the project's Unity version/backend. Existing ChatGenerator components should show Queue For Playback checked. Confirm Save unchecked permits foreground playback and save-only's documented forced save is clear in the Inspector.
2. With a live PBNN generator/context available and another channel playing, explicitly request SaveOnly. Verify JSON appears, successful telemetry waits for persistence, and the viewed context, scene, audio and playback queue remain unchanged through generation and multiple 15-second sweeps.
3. Repeat with PBNN active and a current episode playing. Discovery should catalog the file without inserting it; ordinary queue-empty replay can select it. Return from another channel and verify ownership. Reconfigure/restart and repeat using actual persisted JSON.
4. Run Queue Fixture Bulletin: foreground generation must save and queue exactly once. Validate real generation, audio, save and replay end-to-end; that Unity validation remains outstanding.
5. On deployment storage, exercise first save, replacement, unwritable storage and disk-full/publication failures. Confirm File.Replace backend/filesystem support, failure logs, no success or queue on failure, preservation of prior committed output, and cleanup/ignored staging after termination. Check write latency with real episode/audio sizes. Standalone tests do not establish Unity API compatibility or abrupt-shutdown durability.
