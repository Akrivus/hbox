# PBNN Unity handoff

## Current direction — 2026-10-18

The [studio roadmap](PBNNStudioRoadmap.md) is the current priority order. Finish the physical newsroom, camera framing, local displays and casting exploration before deciding content/playback boundaries. The experimental saved rundown/director, separate Studio Rundown generator, placeholder multi-cast prompts and desk-to-lounge transitions were removed. Chat versus scene versus segment remains undecided; one Chat per bulletin is not a general requirement. Do not restore deferred orchestration to test a room.

The user has played a generated fixture successfully, but its camera angle needs refinement. Replay consistency, skip cleanup and channel-return cleanup still need explicit Unity confirmation. Static checks and Studio Preview are not substitutes for that sweep.

## Current scene and cast

`Assets/Scenes/PBNN.unity` uses context object `PBNN`, context key `pbnn`, scene path `Scenes/PBNN`, config `pbnn.json` and saved episode folder `Documents/PBNN`. Main and build settings register the channel. Runtime prompt routing uses `pbnn`, independently of the scene object name.

The context's `SpawnPoints` and `Locations` register the same five interior IDs. Preserve those IDs; design labels are for documentation/UI. Each group has anchor, target, target-group and one non-null virtual-camera reference. Registration supports episode-level staging, not segment-by-segment switching. Shared camera-target wiring is not a portable mode framework.

| Location ID | Design label | Registered slots | Existing shot | Still to author/verify |
| --- | --- | --- | --- | --- |
| Desk | Anchor desk | 2; first named Business Man 1 | Wide | Refine fixture angle, host placement, TV and lower third |
| Lounge | Conversation lounge | 1 | OTS L | Multiple conversation seats and framing |
| Office | Production office | 1 | Close Up | Crew blocking and concrete workplace dynamics |
| Corner | Presentation corner | 1 | Two Shot | Presenter/questioners and board focus |
| Video Wall | Big picture room | 1 | OTS R | Visible regions, crops/panels and presenter focus |

The studio floor has no registered program location and remains postponed. Only the Desk fixture is the working playback baseline; the other rooms are registered starting points, not visually verified formats.

The inventory contains **60 actor definitions** with identities, prefab references, pronouns and provisional Gemini audition voices. Only **Business Man 1** is in the active scene cast. Use other assets as temporary preview stand-ins without registering them in production. Final anchors and production crew are separate ensembles; guests/presenters depend on format. Author matching `Actors/<Name>.md` profiles and format prompts before expanding generated casting: current templates still mandate one host and a short soccer bulletin. Voices and personalities are not finalized.

## Verify the studio independently

1. Open PBNN in Edit mode. Inspect all five registered groups, their spawn transforms, anchor/target/target-group and camera references. Desk must retain its named stationary Business Man 1 spawn and sole Wide shot. Do not replace the five-location registration with a single temporary staging group.
2. Use [Studio Preview](#studio-blocking-preview-no-chat) for each location and shot. Try existing actors, authored poses and spawn overrides to evaluate placement, sight lines and framing without generating episodes. Add missing seats/presenter positions as actual room needs become clear.
3. Inspect displays with local images: Desk TV framing, board readable area and useful regions of the curved wall. Only the Desk TV and headline/source/date overlay are connected to runtime media playback. Preview does not drive media cues; a readable board/wall image does not establish runtime support for that surface.
4. Run **HBOx → PBNN → Validate Media Scene** outside Play mode for reference, shader, fixture decode and actual Chat JSON round-trip checks. This does not save the scene or prove visible framing/cleanup. See [media handoff](PBNNMediaHandoff.md) for the implemented payload and TV details. Its older single-staging helper/four-wall next-step notes are historical; the current roadmap and five-location workflow here take precedence.
5. Validate Game-view selection and one effective rendered camera/audio listener from Main in Play mode using the retained fixture below. Studio Preview does not simulate camera blends, gameplay IK, speech or runtime lifecycle.

## Run the retained fixture regression

1. Import and compile in Unity. Run **HBOx → PBNN → Install Fixture Prompts** to copy six tracked templates from `Docs/PBNN/Prompts` to `Vault/pbnn/Prompts`. Re-running overwrites those fixture templates; do not replace them with multi-cast placeholders.
2. Open `Assets/Main.unity`, enter Play mode and select **PBNN**, so shared ChatManager/channel lifecycle is initialized. Confirm the context resolves `pbnn.json` and configured LLM/TTS providers.
3. Queue **HBOx → PBNN → Queue Media Fixture Bulletin** once. It uses `Docs/PBNN/FixtureBulletin.txt` through normal generation/TTS/save/playback, with no live soccer/Reddit acquisition. **Queue Fixture Bulletin** is the media-free comparison. Both persist Desk and the Business Man 1 host spawn; older saved episodes are not rewritten.
4. Confirm Business Man 1 appears at Desk with intelligible speech/subtitles and the intended Wide framing. The historical bulletin identifies October 7, 2026 and Scotland 0–0 Netherlands, report `c6bc1932bc604687ba737f9b83811788`. It must not invent match footage, incidents or player quotes.
5. In the media fixture, confirm the middle speaking turns show the local illustration on the Desk TV, without stretching or recoloring its frame, with headline/source/date overlay and subtitle clearance. Opening and closing must clear the image/overlay. The other TV, board and curved wall are not driven by this fixture.
6. Inspect the completed JSON under `Documents/PBNN`: `Key: pbnn`, Business Man 1, saved Desk/host placement, original Idea, playable audio and `pbnn.media.v1` for the media fixture. Generation completion is not save-success confirmation; `Chat.Save()` exposes no success event.
7. Replay the saved episode and confirm the same placement, image, attribution and cue boundaries. During the image, test skip and switching to polbots; no stale picture/overlay, stranded camera/audio or duplicate actors may remain. Return and replay, then play a media-free episode to check stale-content cleanup.

Optional API input remains `POST http://localhost:6789/api/channels/pbnn-default/ideas` with raw UTF-8 fixture text after selecting PBNN. Prefer the menu commands for the saved staging/media markers.

## Validation status and unfinished capabilities

The original shell and bounded media slice have static/compile/serialization checks documented in the [media handoff](PBNNMediaHandoff.md). The user subsequently played a generated fixture; that does not confirm the full regression sweep above. Record actual Unity import/shader, visible display/framing, replay, skip and channel-return results separately. Studio Preview cleanup also needs Editor confirmation when saving, entering Play, reloading scripts, closing the scene or disabling preview.

Other rooms still need usable multi-person blocking and camera/display checks. Final casting, performance profiles and format prompts remain unfinished. Live ingestion, source/rundown contracts, scheduling, staffing/rotation, transition orchestration and advanced walking/IK remain later work. No new segment contract or production generation behavior is required to finish this studio pass.

## Studio blocking preview (no Chat)

Open PBNN directly in Edit mode and select the PBNN context. Its **Studio Preview** component starts disabled. Enable **Preview Enabled**, choose a **Location**, and use **Fill location spawns from context cast** to populate its spawn slots. Replace individual Actor assets to try another cast; repeated actors are allowed. **Spawn Override** accepts any Transform, including global points outside the POI hierarchy. Temporary actors follow moved spawn points.

Click **View selected shot in Scene view** after selecting a shot or moving camera/spawn references. This evaluates the registered Cinemachine camera with the preview cast, frames the Scene view, then restores the camera/target transforms. It does not run the Game view broadcast or generate a Chat. Camera movement/blends and gameplay IK are not simulated. Optional Pose clips are sampled only on the temporary actor copies; otherwise the prefab's authored pose is used. Existing prefab mesh/costume choices are retained.

Saving, entering Play mode, reloading scripts, closing the scene or disabling the component removes the temporary actors. Preview enablement is reset when saving/entering Play. Cast/location choices are ordinary serialized authoring settings. For RomeBots or another IP, select its context and use **Tools → Studio → Add Preview to Selected Context**, then use its registered locations, cameras and Actor assets. Only one context previews at a time.

## Historical architecture and initial shell inventory

The following snapshot preserves useful asset provenance and implementation history. Its 46/28/18 actor counts, Walt/Home Office staging, octagonal four-wall rig, hard-cut/rotation proposals and pending first-play assumptions are superseded by the current sections above. They are not current scene descriptions, acceptance criteria or next-step instructions. Original one-Chat fixture behavior does not define future content boundaries.

### Historical newsroom proposal (2026-10-08)

The copied AppyDays office is temporary fixture staging. Its large office-pack demo layout is not the intended PBNN studio. The supplied blocking concept specifies one compact octagonal studio with four permanent sets on the cardinal walls: News, Weather, Sports and Features. Diagonal faces provide lighting/equipment space, entrances or additional displays. Change set identity through screens, lighting and graphics rather than rebuilding physical sets. Geometry behind the walls need not be camera-visible or realistic.

Use a central camera rig with four authored orientations at 90-degree intervals. A wall enum can map to `targetYaw = (int)wall * 90f`, with a calibrated base yaw if the scene's forward direction differs. Author actor positions, set distance, lens and framing together; yaw alone does not guarantee the shot. The one-Chat-per-bulletin proposal is now deferred; scene/segment/Chat boundaries remain open. Keep actor positions usable for independent studio testing. Existing fixed virtual shots can express these cardinal views without requiring literal camera travel. Start with hard cuts. Smooth rotation or a graphic wipe can be added later, hiding travel behind a transition rather than rotating across the studio during speech. Personnel can rotate through the four stations without requiring a separate set for every character. IK gestures follow proven blocking and framing.

`Assets/Scenes/PBNN/Prefabs/HumanoidBase.prefab` is now the shared humanoid/IK foundation. The user migrated the actor prefabs to variants of that base and added more character meshes. The PBNN actor inventory now contains 46 matching prefab/ScriptableObject pairs. `Character_` (including `CharacteR_`) and `SM_Chr_` prefixes were removed from prefab filenames, root names, actor filenames and asset names; existing GUIDs were preserved. The 28 newly created actor objects use the simplified model names as temporary Name/Title/Alias values, while the existing 18 retain their casting fields. Each actor object references its corresponding prefab. These inventory objects are not automatically registered as the broadcast cast. Final casting, voices and rig/deformation validation remain separate work.

The proposed roster is approximately eight characters with an equal gender split, covering anchors/interviewers and sports, entertainment and political commentary. Paul Botz is a proposed political analyst; he need not be the sole host. Pete Stellaris and Livia Drusilla are proposed additions from SpaceDrivel and RomeBots. Their existing source-channel actor assets remain separate from any future PBNN actor definitions so runtime context ownership stays isolated. AppyDays survivors may become the production team; backstage airtime is undecided. Caller testimony can precede interview/field-report work. Internet weather uses a large graphic board and a presenter rather than requiring a literal forecast feed.

Character dynamics are creative notes, not implemented runtime behavior: Pete and Livia can share a desk with conspicuous romantic tension; a sports commentator challenges colleagues to take it outside and is routinely ignored. Do not assign that sports role to a named character until casting is chosen.

The user supplied the four-wall/octagonal blocking excerpt directly after conversation retrieval omitted that discussion. This records the earlier direction only; the current five-location studio supersedes the octagonal/cardinal-wall plan. A central rig and transitions are not active requirements.

### Original implemented shell (historical snapshot)

`Assets/Scenes/PBNN.unity` is an independent copy of AppyDays staging. Its context object is `PBNN`, key `pbnn`, scene path `Scenes/PBNN`, config `pbnn.json`, and saved episode folder `Documents/PBNN`. Main's channel list and build settings now register it. Local `hbox.json` enables `polbots` and `pbnn`; the example config includes PBNN too. AppyDays remains available as a reference.

The sole placeholder host is Walt. `Assets/Scenes/PBNN/Actors/Walt.asset` copies his actor settings while retaining the existing `Walt.prefab` visual and `en-AU-Standard-D` voice. A separate actor definition prevents another loaded context from overwriting its runtime ManagerContext. The original AppyDays actor is unchanged. Paul Bott's identity, final visual and voice remain creative work.

The Default generator runs behavior, editor, dialogue, sentiment, speech and title generation in that order. Editor inference is disabled; the scene uses the prepared rundown instead of a second story rewrite. Sentiment annotation preserves spoken wording. Reddit intake, codex/memory writers, episode continuity, dialogue rewrite and random background music were removed from this copy. Existing shared LLM/TTS credentials and provider configuration still apply. PBNN's config clears inherited Discord webhooks and disables bot/idea commands so fixture playback has no Discord publishing destination.

One Chat contains the entire fixture bulletin. It goes through the existing Idea queue, generation, TTS, save and playback flow. This slice does not add a typed source packet or segment director.

### Original reusable asset inventory

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

The original scene has 22 registered POI managers. PBNN registers only Home Office for this slice. The remaining copied set objects are staging inventory, not segment routing. The proposed P3 cubicle cutaways and saved-boundary routing were superseded by the October 18 studio-first reset; do not implement them as the next slice. A single eligible virtual camera makes selection deterministic; it does not yet guarantee a stationary composition when its Cinemachine follow/aim components are active.
