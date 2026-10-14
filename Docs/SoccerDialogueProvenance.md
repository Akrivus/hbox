# Soccer dialogue provenance (N2c)

Completed soccer reports optionally contain `Reactions`, independently of authoritative
`HomeScore`, `AwayScore`, and `KeyEvents`. A reaction records an attributed fictional
character statement; its contents cannot establish that a claimed event happened.
Historical JSON without `Reactions` loads unchanged. An absent field means no historical
capture metadata, not proof that nobody spoke. New matches can have an empty array.

## Capture boundary

`OnChatNodeActivated` is a presentation notification before actor activation and is not
used to record quotes. `ActorController.Activate` returning is also insufficient:
confidence timing can finish early, and async nodes return while audio continues.

Immediately after `AudioSource.Play`, the actor verifies `isPlaying`, snapshots the
constant positive pitch and DSP start time, and runs a separate observer. The manager
publishes immutable `DialoguePlayback` start/completion snapshots. Completion requires
that the same source and clip stop playing no earlier than their sample-duration divided
by pitch, with the actor activation and chat/context generation still current. Early
stops, explicit interruptions, replacement activations/clips, skip, changed pitch,
muting, disabled actors, and stale contexts reject the line. Missing/failed audio starts
and looping/nonpositive-pitch sources never qualify. Merely generating a node or its
TTS does not create a reaction. Partial lines are omitted rather than represented as
complete quotations. No confidence-based scheduling or async wait behavior is extended.

`SoccerReactionCapture` binds the match at audio start, accepting only the owning host
channel and rejecting explicit interrupt metadata for another match. Ordinary host
dialogue heard while the match is active gets that match association; it is contextual
commentary, not necessarily about a particular authoritative event. Interrupt nodes are
stamped with the actual packet `MatchId` and `PacketId` before injection. Completion
without an accepted start cannot create a record or carry a quote into a later match.

The final whistle snapshots completed lines into the existing report write and closes
capture. Pending lines that finish later and separately queued postgame episodes are
excluded. Cancelled/unloaded matches produce no new completed report. This change does
not alter report write/retry behavior. Announcer-service audio, screenshots, PBNN
adapters, newsroom rendering, and replay history are outside this implementation.

Each record preserves:

- `MatchId`, unique `PlaybackId`, original `ChatNode.Actor` speaker name.
- Exact `SpokenText` from `Say`, plus `OriginalDialogue` from `OriginalText` or `Text`.
  Stage directions in the original source are not represented as spoken words.
- `SourceChatId` (saved filename, or a runtime episode identifier), node index at
  activation, and optional `SourceInterruptId`. A node index is a snapshot: injection
  can shift later indices. Runtime IDs are correlation IDs, not promised saved assets.
- UTC wall-clock start/completion and Unity DSP start/completion seconds. Completion
  is the first frame observing the end, not a sample-accurate timestamp. DSP seconds
  are process-local; wall-clock timestamps can correlate persisted reports.

## Deterministic checks

With .NET 10 and the project's resolved Newtonsoft.Json DLL:

```powershell
./Tests/Soccer/Run.ps1
dotnet run --project Tests/DialoguePlayback/DialoguePlaybackTests.csproj
```

The soccer harness tests exact text/provenance, completion gating, deduplication,
channel/match isolation, final-whistle closure, serialization, and actual store reload
of mixed historical/enriched reports. The playback harness compiles the production
`ActorController` and observer against controlled Unity stubs. It tests async and sync
scheduling, natural completion, skipped/interrupted/missing/failed playback, clip
replacement, context invalidation, delay/TTS interruption, muting, and pitch handling.
These checks simulate Unity's audio observations; they do not run an audio device.

## Unity verification still required

Run one short live fixture in the Unity Editor/build with working audio:

1. Hear a full normal line and a soccer interrupt finish. Check the saved report's exact
   `Say`, speaker, match/chat/packet IDs, and start/end times after final whistle.
2. Interrupt a long line mid-speech, skip another, replace an async line on the same
   actor, and switch contexts while speaking. Confirm none produces a complete quote.
3. Try missing TTS and paused/muted/replaced audio. Confirm no incomplete line is stored.
   Check both confidence-shortened scheduling and async overlap with distinct actors.
4. Start speech before final whistle and finish it afterward; confirm it is excluded.
   Reload a historical report and the new report through the event store.

The boundary relies on Unity's `isPlaying` transition plus continuous DSP elapsed time,
not microphone measurement or proof of speaker/device audibility. Verify on the target
Unity version that natural stops, `AudioSource.Stop`/Pause, pitch, focus loss, listener
pause, and mixer routing behave as expected. Pause/Stop that occurs and disappears
entirely between polls, a seek, or an unobserved mixer mute cannot be distinguished
reliably by frame polling. AudioSource/global listener mute/volume are checked; mixer
routing and output-device delivery are not observable here. Do not mark live playback
verification complete based on the standalone harness. If a platform cannot establish
this boundary, keep reaction capture conservative rather than accepting activation.
