# Soccer Implementation and Unity Handoff

S1 through S4 can be prepared from code and serialized scene data. The remaining boundary is Unity's actual camera composition, layout, match lifecycle, and generated performance. This guide covers the current polbots scene, not the archived Soccer Mode scene.

## Prepared work

| Task | Prepared autonomously | Remaining Unity confirmation |
| --- | --- | --- |
| S1 Existing handoff | Traced topic/idea mode matching, AfterIntermission trigger, additive football loading, host-scene preservation, packet interrupts, and teardown. Existing generator, team, audio, UI, and fallback spawn references are present. | Start from Main, enter a soccer scene, observe one interrupt, leave and return. |
| S2 Background and heads | Added opt-in direct football camera output, a direct-output watchdog path, camera depth/viewport restoration, hidden media box, and a right-side single-column rail capped at three or four heads. Existing speaker promotion remains. | Confirm the game is visible behind the overlay, head dimensions fit, and call/globe layouts restore. |
| S3 Scheduled events | Added config-driven one-off events and a persisted dispatch ledger. Due matches queue only while polbots is being viewed and the soccer generator is available. One outstanding scheduled event blocks further dispatch. | Supply participant names and one timestamp; verify generation, kickoff, one-time dispatch, and overdue dispatch after returning to polbots. |
| S4 Results and continuity | Final whistle writes an atomic structured report. Existing soccer continuity now includes highlights. Main polbots context explicitly includes `#soccer-live` in generation recall. Added GET `/api/soccer/reports?limit=50`. | Complete a match, inspect report/API, then generate a later scene and confirm factual recall. PBNN consumption waits for PBNN implementation. |

The changes use existing scene references. No new component needs dragging into the scene. The current polbots context has its Shared Memory Buckets field wired to `#soccer-live` in scene serialization. Full-screen display and scheduled events are opt-in; existing configurations keep their current presentation and startup behavior.

## Enable the first full screen match

The full-screen camera path has been confirmed working in Unity. Follow-up UI changes hide the call footer and episode title during the match and fade the top-left event feed over one second. Normal visibility returns on exit; the feed still follows its existing new-episode/replay rules. These overlay changes need the next visual confirmation.

These decisions now live in the shared [UI Visibility Rules](UIVisibility.md) policy and coordinator. Widgets retain fades and title timing, while globe/soccer/playback conditions are combined centrally. The coordinator installs itself on ChatManager; no new manual wiring is needed.

Postgame ideas are now marked with `soccer-postgame` source and `SoccerPhase: Postgame`. Original soccer metadata carried inside their story context cannot start another match or regenerate a soccer roster. `GameOnStart` remains latched after a successful match instead of resetting during teardown. RequireTextPatternMatch can remain true, but these explicit guards handle postgame restart separately. Previously saved unmarked postgame scenes are not migrated.

Startup troubleshooting: stop play mode completely and restart from Main after the ownership fix. Main preloads and then reloads polbots; soccer sources now remain with their own contexts and the current context claims ownership, rather than destroying its new component because a preload instance survived. Old sources no longer react to another context's football events or scene unloads. Auto-start can select current actors without fixed legacy player lists.

The active `Soccer` object's SoccerGameSource now exposes **Startup Status** in the Inspector and logs stage changes with `[Soccer]`. With GameOnStart enabled, expect configuration/ownership, loading football scene, waiting for MatchEngineLoader, creating simulator match, then Match live. A mode-triggered scene additionally logs Mode matched and participant resolution. Markdown formatting around Mode/Home/Away metadata is accepted. Mode text in ordinary prose is not itself a trigger.

If nothing loads, report the last `[Soccer]` line or Startup Status. In play mode the component menu also offers **Toggle soccer match**, which bypasses topic selection to isolate loading from generation. Use the component under the current polbots context, not an older DontDestroyOnLoad context. Missing team assets, missing build-scene registration, strict metadata rejection, and unresolved participants now have explicit statuses. Source compilation and metadata checks pass; the ownership fix still requires a fresh play-mode attempt.

1. Open the project and let Unity import/compile. Open `Assets/Main.unity` for play mode.
2. In `polbots.json`, locate the existing object with `"Type": "soccer"`. Add `"FullScreenMatch": true` and `"FullScreenHeads": 4`. Keep its other fields, including event templates. Config changes load on startup; restart play mode after editing.
3. Start a match through the existing soccer flow. The `Soccer` scene object already has its `SoccerGameSource` generator, team assets, crowd/announcer audio, and legacy broadcast texture assigned. Keep those references.
4. Confirm football fills the background, the media box is hidden, and four heads form one right-side column. Three heads can be selected with `FullScreenHeads: 3`. Speaker promotion should still bring a speaker into the visible group.
5. Confirm a goal or other interrupt still injects dialogue, and that match/voice audio remain audible without a duplicate AudioListener warning.
6. Exit the match, then check Discord/call and globe staging. Repeat a match and try a channel/scene transition to exercise teardown. Existing football scenes and runtime objects should disappear.

The serialized polbots canvas is Screen Space Overlay. The direct path renders the football camera after other cameras that render to the display, leaving actor head cameras on their existing textures. This removes the match broadcast render texture; it does not remove the head textures.

If the background is covered, inspect the existing VideoCallComponent's full-screen decorative Images and their opacity. If the head rail does not fit, adjust its existing `Video Screens` grid cell size, spacing, and padding for the target resolution. Current prefab cells are 360 by 210 with spacing 10; the new path supplies a single column and right alignment automatically. Visual tuning belongs in Unity rather than guessed prefab coordinates.

Set `FullScreenMatch` back to false to compare with the existing media-share path. Do this between play sessions, not halfway through a match.

## Configure recurring matches

`Schedules` is an array of recurring soccer schedules. Each ID has independent recurrence, lore, pending matchup, and result history. Remove old `ScheduledMatches` and `Tournament` entries, and wrap your old `RollingSchedule` object in the `Schedules` array. Replace the old `StartsAt` and `MinutesBetweenMatches` fields with `Recurrence`. No scene wiring is needed.

For Wednesday and Friday at 7pm Phoenix time:

```json
"Schedules": [{
  "Id": "polbots-football",
  "Name": "Polbots Football",
  "Recurrence": {
    "Days": ["Wednesday", "Friday"],
    "Times": ["19:00"],
    "TimeZoneId": "US Mountain Standard Time"
  },
  "WinnerStays": false,
  "Lore": "Countries drag their current arguments onto the pitch, and nobody lets a previous defeat go quietly."
}]
```

Add more objects to this array for additional schedules, using distinct IDs. Due fixtures share one match/generation slot; the oldest due fixture goes first. A queued or started match blocks other schedules until completion/cancellation. Keep existing IDs when migrating to preserve their saved history.

Alternatively use `"Recurrence": { "EveryMinutes": 60 }`. The first match becomes due one interval after the schedule is first registered. Subsequent intervals start at completion or cancellation. Calendar patterns choose the next matching local time strictly after registration/completion. `Times` uses 24-hour HH:mm; provide `Days` for calendar patterns. Times take precedence over EveryMinutes. Named timezones handle daylight saving; nonexistent spring-forward slots are skipped and repeated autumn times use one standard-time occurrence.

Keep `GameOnStart`, `GameOnBatchEnd`, and `GameOnMatchEnd` false if matches should only come from this schedule or explicit dialogue requests. Keep polbots selected and unpaused. Due matches wait for generator availability; kickoff follows generated chat playback. This is a generation schedule, so it does not guarantee kickoff at the exact clock time.

There is one pending fixture at a time. Returning after missed slots generates one catch-up match rather than a queue of missed matches. The next slot is calculated after that match completes. Random distinct participants come from the current nonlegacy actors with prefabs; generated rosters supply their players. New actors can enter subsequent games. Selection avoids the previous pairing when alternatives exist. `WinnerStays: true` optionally keeps the last winner against a challenger; draws have no invented winner.

Prompts carry the latest eight recorded series results and winners, the recurring premise, and current polbots discussion. Reports and series history remain available for PBNN. A pending fixture and its participants survive reload; recurrence determines future fixtures. Existing saved pending matches and NextAt values are honored once, then use the new pattern. Changing a pattern while waiting does not reschedule the already persisted next slot.

If generation fails or play stops midmatch, the outstanding event remains blocked to prevent duplicate generation. Use **Cancel outstanding soccer schedule events** on SoccerGameSource when no generation/match is active; the next occurrence gets a fresh event ID. Old tournament files and reports are retained, but tournaments and date lists no longer dispatch.

For a quick check use EveryMinutes 1, restart before it becomes due, and confirm the saved due time survives. Complete one match and check the next result-history prompt. Then switch to your weekly pattern. Saved state is under `Application.persistentDataPath/soccer/polbots/series/<Id>.json`.

## Inspect the result and memory

Completed reports and the dispatch ledger are stored under:

```text
Application.persistentDataPath/soccer/polbots/schedule.json
Application.persistentDataPath/soccer/polbots/reports/<runtime-match-id>.json
```

On Windows, the persistent-data root normally follows `%USERPROFILE%/AppData/LocalLow/<CompanyName>/<ProductName>`, using this project's Player Settings. Each report records its runtime match ID, scheduled event ID when applicable, host channel, participants, final score, completion timestamp, roster context, and up to 100 injectable event highlights. Reports are available to another generator as files and through the existing server at `/api/soccer/reports?limit=50`; use the same host and port as the existing HBOx web service. The endpoint returns newest completions first and an empty array before any report exists. It does not consume reports or mark them as reported by PBNN.

The existing `#soccer-live` continuity bucket is now included explicitly in polbots generation context. Check the context object's **Shared Memory Buckets** field contains `#soccer-live`. After one completed match, generate a later idea asking the participants about that match and inspect the saved prompt input as well as the dialogue. The prompt should contain the real result; semantic recall by the model still needs this live check.

PBNN is not built yet. Its future source adapter can read the reports and deduplicate by MatchId without acquiring the soccer simulation, channel context, or scene objects.

## Verification completed and limits

- Existing soccer roster checks pass; additional tests cover due-versus-future selection, ledger persistence and duplicate prevention, completion releasing the next event, report round-trip data, path-safe IDs, automatic round-robin pairings, fixture intervals, saved entrant stability, and result-derived lore/standings. Rolling schedule checks cover timing, new entrants, immediate rematch avoidance, winner/draw history, and pending fixture stability through reload.
- Existing context tests pass for prompt/file isolation and soccer context serialization.
- Assembly-CSharp source compiles in a temporary SDK project against the installed Unity/package assemblies with zero errors. Existing code produces warnings. This validates source compatibility against those references, not Unity's import pipeline or a player build.
- The native generated project build hits a restricted Windows SDK lookup. The temporary compilation avoids that lookup without changing Unity's generated project files.
- No live match or Unity visual pass has been performed. Camera composition, UI appearance, scheduler-to-generated-chat behavior, endpoint behavior in the running service, and model recall remain the play-mode checks above.

Send the first failing step, relevant Console output, and any Inspector reference that is missing. Further code work can then be isolated to that failure; manual visual adjustments stay in the existing scene and prefab.

## Confirmed progress

S1 and S2 are confirmed complete by the user. S3 configuration is complete; trigger one recurring event and confirm its scheduled event ID and series ID appear in its completed report. The supplied Scotland–Netherlands 0–0 report (`c6bc1932bc604687ba737f9b83811788`, 2026-10-07 18:11:44 Phoenix time) demonstrates report creation, but its null event/series IDs do not verify scheduling. S4 PBNN consumption awaits the PBNN channel. Reaction excerpts and firsthand screenshots are defined as optional follow-up work in ChannelBacklog.md (N2c).
