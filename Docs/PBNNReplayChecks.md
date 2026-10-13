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

Validation in the implementation environment: all 14 regression checks passed
under .NET SDK 10.0.100 with zero build warnings/errors. Whitespace checks passed
with CRLF recognized for the existing Windows-formatted files.

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
6. Point PBNN to an empty test directory; confirm waiting without generation,
   exceptions or queue churn, then add a playable saved bulletin and verify the
   existing incoming-file sweep picks it up.

Live Unity generation, successful saving and playable replay remain unverified
by the standalone harness. Its tiny saved fixtures intentionally contain no
audio/actors. N3 source behavior can be tested independently of N2 source
persistence and live news ingestion.
