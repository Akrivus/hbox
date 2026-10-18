# PBNN P0/P1 Unity handoff

Current audit: see [Studio roadmap — 2026-10-17](PBNNStudioRoadmap.md). The user has now played a working fixture. All five interiors are registered, the inventory has grown to 60 voiced actors, and the next design uses distinct anchor, conversation, production and visual-explanation loops. The initial inventory and casting notes below are implementation history; the current roadmap supersedes their counts, old office references and pending first-play status.

## Current casting inventory

All 46 PBNN actor ScriptableObjects now use their descriptive filenames as `Name`, `Title` and sole alias. Shared Walt/X aliases were removed. These are provisional feature/interview casting identities; final personalities and voices are still open. Prefab references and asset GUIDs are preserved. The single-host fixture now uses **Business Man 1**, with matching prompts and a repaired scene actor reference. Only that host is registered in the fixture scene; the other inventory actors are not automatically added to the broadcast cast. Earlier Walt references in the initial-shell inventory below describe the original placeholder setup, superseded by this casting update. Old episodes naming Walt will need an explicit migration if they are retained.

## Updated newsroom and character direction (2026-10-08)

The copied AppyDays office is temporary fixture staging. Its large office-pack demo layout is not the intended PBNN studio. The supplied blocking concept specifies one compact octagonal studio with four permanent sets on the cardinal walls: News, Weather, Sports and Features. Diagonal faces provide lighting/equipment space, entrances or additional displays. Change set identity through screens, lighting and graphics rather than rebuilding physical sets. Geometry behind the walls need not be camera-visible or realistic.

Use a central camera rig with four authored orientations at 90-degree intervals. A wall enum can map to `targetYaw = (int)wall * 90f`, with a calibrated base yaw if the scene's forward direction differs. Author actor positions, set distance, lens and framing together; yaw alone does not guarantee the shot. Keep one Chat per bulletin and stable actor positions. Existing fixed virtual shots can express these cardinal views without requiring literal camera travel. Start with hard cuts. Smooth rotation or a graphic wipe can be added later, hiding travel behind a transition rather than rotating across the studio during speech. Personnel can rotate through the four stations without requiring a separate set for every character. IK gestures follow proven blocking and framing.

`Assets/Scenes/PBNN/Prefabs/HumanoidBase.prefab` is now the shared humanoid/IK foundation. The user migrated the actor prefabs to variants of that base and added more character meshes. The PBNN actor inventory now contains 46 matching prefab/ScriptableObject pairs. `Character_` (including `CharacteR_`) and `SM_Chr_` prefixes were removed from prefab filenames, root names, actor filenames and asset names; existing GUIDs were preserved. The 28 newly created actor objects use the simplified model names as temporary Name/Title/Alias values, while the existing 18 retain their casting fields. Each actor object references its corresponding prefab. These inventory objects are not automatically registered as the broadcast cast. Final casting, voices and rig/deformation validation remain separate work.

The proposed roster is approximately eight characters with an equal gender split, covering anchors/interviewers and sports, entertainment and political commentary. Paul Botz is a proposed political analyst; he need not be the sole host. Pete Stellaris and Livia Drusilla are proposed additions from SpaceDrivel and RomeBots. Their existing source-channel actor assets remain separate from any future PBNN actor definitions so runtime context ownership stays isolated. AppyDays survivors may become the production team; backstage airtime is undecided. Caller testimony can precede interview/field-report work. Internet weather uses a large graphic board and a presenter rather than requiring a literal forecast feed.

Character dynamics are creative notes, not implemented runtime behavior: Pete and Livia can share a desk with conspicuous romantic tension; a sports commentator challenges colleagues to take it outside and is routinely ignored. Do not assign that sports role to a named character until casting is chosen.

The user supplied the four-wall/octagonal blocking excerpt directly after conversation retrieval omitted that discussion. This records the intended studio direction; physical studio replacement, central rig and transitions are not yet implemented.

## Implemented shell

`Assets/Scenes/PBNN.unity` is an independent copy of AppyDays staging. Its context object is `PBNN`, key `pbnn`, scene path `Scenes/PBNN`, config `pbnn.json`, and saved episode folder `Documents/PBNN`. Main's channel list and build settings now register it. Local `hbox.json` enables `polbots` and `pbnn`; the example config includes PBNN too. AppyDays remains available as a reference.

The sole placeholder host is Walt. `Assets/Scenes/PBNN/Actors/Walt.asset` copies his actor settings while retaining the existing `Walt.prefab` visual and `en-AU-Standard-D` voice. A separate actor definition prevents another loaded context from overwriting its runtime ManagerContext. The original AppyDays actor is unchanged. Paul Bott's identity, final visual and voice remain creative work.

The Default generator runs behavior, editor, dialogue, sentiment, speech and title generation in that order. Editor inference is disabled; the scene uses the prepared rundown instead of a second story rewrite. Sentiment annotation preserves spoken wording. Reddit intake, codex/memory writers, episode continuity, dialogue rewrite and random background music were removed from this copy. Existing shared LLM/TTS credentials and provider configuration still apply. PBNN's config clears inherited Discord webhooks and disables bot/idea commands so fixture playback has no Discord publishing destination.

One Chat contains the entire fixture bulletin. It goes through the existing Idea queue, generation, TTS, save and playback flow. This slice does not add a typed source packet or segment director.

## Reusable asset inventory

| Item | Existing reference | P0/P1 use |
| --- | --- | --- |
| Office environment | `Assets/Scenes/AppyDays.unity`: MainOffice, HomeOffice, Stage, lighting and rendered Main Camera | Copied scene retains the set and references; final newsroom dressing remains open |
| Anchor visual | `Assets/Scenes/AppyDays/Prefabs/Actors/Walt.prefab` | Shared visual, isolated PBNN actor definition |
| Other candidate actor definitions | `Assets/Scenes/AppyDays/Actors`: Clara, Clive, Diane, Donovan, Ethan, Felicity, Gideon, Howie, Ivy, Janine, Marcus, Marcy, Morgan, Noah, Ricky, Sal, Vanessa | Inventory only; not in the PBNN fixture cast |
| Other humanoid prefabs | `Assets/Scenes/AppyDays/Prefabs/Actors` | Reusable visual candidates; actor and prefab names do not always match, so follow each actor's Prefab reference |
| Studio spawn group | Home Office POI, scene SpawnPointManager fileID `1468270354`, prefab `Assets/Core/Integrations/Location/POI.prefab` | Sole eligible group; its nine spawn entries are stationary in the PBNN copy |
| Initial virtual shot | Two Shot, scene virtual-camera fileID `137251396` | Sole eligible camera for the active group; framing and follow behavior still require editor inspection |
| Actor animation/voice | Existing Walt prefab Animator/ActorController and copied voice settings | Uses existing talk/idle behavior and configured TTS provider |
| Replay collector | Existing FolderSource under Initializer | Configured for `PBNN`, one replay per batch, 24-hour preferred age and history size 80 |

The original scene has 22 registered POI managers. PBNN registers only Home Office for this slice. The remaining copied set objects are staging inventory, not segment routing. P3 will author two cubicle shots and explicitly route their saved boundaries. A single eligible virtual camera makes selection deterministic; it does not yet guarantee a stationary composition when its Cinemachine follow/aim components are active.

## Run the fixture

1. Let Unity import the new assets and compile. Run **HBOx → PBNN → Install Fixture Prompts**. This copies the six tracked templates from `Docs/PBNN/Prompts` to the runtime `Vault/pbnn/Prompts` directory. They are already installed in this workspace; the menu makes setup reproducible on another checkout. Re-running it overwrites those fixture templates.
2. Open `Assets/Main.unity`, enter Play mode, and select **PBNN** from the channel guide. Start from Main so the shared ChatManager and channel lifecycle are initialized.
3. Run **HBOx → PBNN → Queue Fixture Bulletin** once. The action requires the viewed channel to be PBNN and refuses another queued/in-progress generation. It reads `Docs/PBNN/FixtureBulletin.txt`; no live soccer request or Reddit review runs.
4. Confirm one Walt anchor appears in the Home Office staging, with intelligible speech and subtitles. The historical bulletin should identify October 7, 2026 and report Scotland 0–0 Netherlands from report `c6bc1932bc604687ba737f9b83811788`. It must not invent player quotes, captured images or match incidents.
5. Confirm a JSON episode appears in `Documents/PBNN` with `Key: pbnn`, Walt as its actor, the original Idea and playable audio data. Generation does not provide a save-success event; inspect the completed file rather than treating its completion log as write confirmation.
6. Let the existing replay flow play the saved bulletin, then switch to polbots and back. Verify the reloaded bulletin retains PBNN ownership and Walt, with no stranded camera/audio or actor duplication.

Optional API input uses the existing route, with the fixture as a raw UTF-8 text body: `POST http://localhost:6789/api/channels/pbnn-default/ideas`. Select PBNN first for this slice. The menu action is the simpler verification path.

## Exact remaining editor checks

- Inspect the PBNN context's single SpawnPoints entry and Walt actor reference for missing imports. Confirm its ConfigManager points to `pbnn.json`.
- Inspect Home Office and Two Shot in the copied scene. Position the placeholder anchor or adjust that scene's camera for a readable composition if the inherited office framing misses the host. Verify one effective rendered camera/audio listener. Do not edit the shared POI or actor prefab to make PBNN-only adjustments.
- Confirm configured TTS can use Walt's copied voice. Change the PBNN actor asset's voice only if the active provider requires a different ID.
- Run generation, saved replay, skip and channel-return checks above. Final station branding, a desk, a second cubicle and Paul Bott are not required to validate this single-host shell.

## Validation and status

The fixture menu compiled against the installed Unity 6000.3.10f1 libraries and current runtime assembly with zero warnings/errors. Static checks verified unique scene IDs, removal of old component references, isolated host ownership, ordered generator components, channel/build registration, config parsing and installed prompt copies. `git diff --check` passed.

Unity was already open; this task did not run a second editor or perform the live generation/playback sweep. P0 inventory and P1 shell/config/prompts are implemented. The P1 playable/save/replay completion check remains pending the editor sweep. P2 source/rundown persistence and P3 cubicle cutaways are the next slices after that check.
