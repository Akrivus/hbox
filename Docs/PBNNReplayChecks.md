# PBNN N3 replay selection

## Configuration

Merge the folder entry in root `pbnn.example.json` into the existing local
`pbnn.json`. Keep the existing Discord and other PBNN configuration entries.
The context already points to `pbnn.json`; local channel configs are ignored by
git. This example changes no shared or other-channel configuration.

- Preferred age: 24 hours.
- Remembered replay history: 80 entries (not frequency or a hard expiration).
- Replay batch: one bulletin.
- `ReplayOlderAfterRecentExhausted: true`: retain older saved files as candidates.
  Omitted/false keeps other channels' existing selection policy.

## Selection policy

Use eligible episodes first; if every episode is cooling, continue from the
cooling pool rather than stopping the broadcast. Within that pool, choose
episodes absent from remembered rotation history when any exist. Prefer recent
bulletins within that selection, retaining the existing audience vote, freshness
and playback-count ranking within each age tier.

After all saved episodes have been selected, the next remembered selection starts
a new rotation. Selection/status reads do not reset history. Cooldowns remain
preferences, not hard bans. Missing files and blank slugs do not count toward
exhaustion. A completely empty library queues nothing and leaves the existing
queue-empty/sweep lifecycle waiting for content.

As before, selection is remembered at queue time; cooldown and replay count are
updated when playback loads the saved episode. Persisted history is limited by
ReplayRate. This slice does not redesign long-term history retention, save
acknowledgements, source packets or ingestion.

## Replay-history persistence

History updates (including rotation resets) are serialized and synchronously
committed before `AddReplayToList` returns and before the saved chat is queued.
The persisted snapshot keeps the existing `Distinct().TakeLast(ReplayRate)`
semantics and the existing line-based `replays-{channelKey}.txt` format.
Selection policy, votes and manifest persistence are unchanged.

Each update writes a unique temporary file alongside the destination, flushes
its writer and file stream (`Flush(true)`), then uses `File.Replace` for an
existing destination or `File.Move` for its first publication. On filesystems
supporting atomic replacement, interruption leaves either the previous complete
history or the new complete history, never a partially overwritten final file.
Restart reads only the final filename and ignores abandoned staging files.
This is not a guarantee of directory-entry durability through power loss.

Write/publication failures emit a Unity warning and preserve the last committed
file. Playback continues with in-memory history; the next selection retries the
complete current bounded snapshot. A restart after a failed commit resumes the
last successful commit and may repeat a selection. Unsupported replacement is
reported as a failure; there is no destructive delete/copy fallback. Normal
failures clean staging files; process termination may leave ignored `.tmp`
files. Serialization is within this process, not coordination between separate
HBOx processes or independent stale histories for the same channel.

Synchronous I/O deliberately avoids pending writes at channel teardown and app
exit. These small commits can block the Unity thread on slow storage; verify
latency on the deployment filesystem. This change does not modify `Chat.Save()`
or add a second episode-persistence implementation.

## Deterministic checks

`dotnet run --project Tests/Replay/ReplayTests.csproj` compiles production replay
code with Unity/playback boundary stubs. Saved fixtures use an explicit manifest
and fixed October 13, 2026 clock, so filesystem creation times and the machine's
current date cannot change the expected rotation.

Coverage: today/yesterday priority, partial and complete exhaustion, repeated
rotations, case-insensitive history, eligible-versus-cooling selection, exact
cooldown/age boundaries, all-cooling fallback, manifest playback updates, audience
votes, legacy channel defaults, invalid/missing entries, empty libraries and
saved-file queue handoff.

Persistence coverage uses the production writer: 50 rapid updates with bounded
legacy history, deliberately overlapping calls, committed full-rotation reset,
restart from actual writes, staged publication interruption, preservation of
old bytes, ignored abandoned staging, observed staging failures and subsequent
full-snapshot recovery. The publication seam injects an interruption before
rename; it does not simulate forcibly killing Unity or a power failure.

Validation: all 18 regression checks passed under .NET SDK 10.0.100. This
environment could not restore NuGet directly, so a temporary uncommitted project
referenced the SDK-bundled Newtonsoft.Json DLL while compiling the same source,
stubs and fixtures. The normal checked-in project/package reference is unchanged.

## Remaining Unity checks

1. Import/compile and merge the example folder entry into local `pbnn.json`.
   Start Main, select PBNN, and confirm FolderSource receives the opt-in flag.
2. Use two already playable saved PBNN bulletins copied into an isolated test
   replay directory. Set their manifest generatedAt values to today and yesterday
   (more than 24 hours old); do not rely on file-copy creation times. Preserve
   original files and back up local replay history/manifest before testing.
3. With batch size one, observe the recent bulletin, then the older bulletin,
   then another rotation. Confirm original bulletin dates remain understandable
   from saved content; this selector does not add a date graphic or N2 payload.
4. Verify actual OnChatLoaded timing updates counts/cooldowns, and that eligible
   older content wins over cooling recent material. Check the all-cooling case.
5. Skip, switch to polbots and return. Check cast, camera/audio ownership,
   playlist behavior, vote/status display and absence of duplicate actors.
6. On the actual Unity scripting backend/target filesystem, verify
   `StreamWriter(..., leaveOpen: true)`, `Flush(true)`, `File.Replace` and first
   publication `File.Move` import and work. Run rapid replay selections and a
   rotation reset, stop/restart immediately, and check the committed history.
   Confirm acceptable frame latency, channel switching and no pending writer
   during teardown. In an isolated directory, deny writes/publication and verify
   warnings, continuing playback, intact last history and recovery once writable.
   Terminate the test player while staging to check old-or-new complete final
   history and ignored temporary files. Standalone tests cannot establish Unity
   runtime/platform atomicity, actual process termination or storage durability.
7. Point PBNN to an empty test directory; confirm waiting without generation,
   exceptions or queue churn, then add a playable saved bulletin and verify the
   existing incoming-file sweep picks it up.

Live Unity generation, successful saving and playable replay remain unverified
by the standalone harness. Its tiny saved fixtures intentionally contain no
audio/actors. N3 source behavior can be tested independently of N2 source
persistence and live news ingestion.

