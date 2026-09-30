# Episode recordings

OBS recording links are stored in `Recordings/index/<recording-id>.json` under the runtime working directory. This directory is ignored by Git; keep it with your recording archive. Each record contains the channel key, episode slug and title captured before playback, timestamps, status, and the exact `outputPath` returned by OBS. Multiple takes remain separate records. Ratings still belong to their existing episode identifiers.

The operator dashboard's replay cards show recording paths and shared-file labels. `GET /api/replays` includes the same data in each row's `recordings` array.

## Configuration

The existing `obs` config continues to control recording:

- `IsRecording: true` enables automatic recording.
- `DoSplitRecording: true` creates one file per episode using an acknowledged stop/start before the next episode's splash screen. OBS's split command has no output-path response, so it is no longer used.
- `DoSplitRecording: false` keeps a continuous recording and links all included episodes to that file when it stops. These are labeled shared recordings; this does not provide frame-accurate cut points.
- `OnlyNewEpisodes: true` stops recording before replay episodes.
- `OBSWebSocketPassword` is optional unless OBS WebSocket authentication is enabled. Keep it in your local ignored config.
- `VideosFolder` is retained for config compatibility but is no longer scanned. OBS supplies the authoritative path, including for a remote OBS host.

Video filenames are no longer renamed. The catalog is the source of identity. It avoids guessing which video is newest, racing an active file, or assigning the next episode's name to the previous file. Existing videos are not automatically backfilled.

## Failure behavior

Commands are serialized and responses are checked. Playback waits for recording preparation, with a 15-second deadline per OBS request. A failure is logged and playback can continue without a recording.

The catalog journals identity before `StartRecord`, then saves the final path atomically after `StopRecord`. Failed or ambiguous commands are not blindly retried and do not produce guessed links. An already-running recording outside the current session is not adopted or stopped. Stop that recording in OBS before allowing HBOx to start another.

A crash, interrupted Editor play session, manual OBS stop/split, or lost stop response can leave an incomplete/unconfirmed entry. Only `completed` entries have a confirmed path. Do not manually split or restart OBS recordings while HBOx owns them. There is no automatic recovery of an unknown output path after a crash. Paths refer to the OBS machine, and moving/remuxing/deleting a video externally does not update the catalog.

## Verification

Run `./Tests/Recording/Run.ps1` from PowerShell with the .NET 10 SDK and Unity packages resolved. The standalone harness uses the production session/catalog and WebSocket client. It tests split identity, continuous files, channel isolation, multiple takes, restart persistence, command failures, catalog write recovery, authentication, fragmented replies, request IDs, and timeouts against a local mock OBS server.

For a live smoke test, enable recording with splitting, play two episodes, let the queue empty, then compare each replay card's path with its video. Repeat with splitting disabled to verify both episodes link to the same shared file. Confirm an intervening replay is excluded when `OnlyNewEpisodes` is enabled.
