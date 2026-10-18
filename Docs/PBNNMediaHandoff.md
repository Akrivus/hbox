# PBNN local image and lower-third slice

Implemented one saved local image on one wall TV, with a headline/source overlay. This uses the existing PBNN scene and normal generate/save/play/replay flow. It does not require image generation, network media acquisition or a four-wall director.

## Scene wiring

- `PbnnMediaPresentation` is attached to the PBNN context. Its TV reference targets the existing `SM_Prop_TV_Wall_01` under **Desk** (scene prefab instance `2033724431`, world position approximately `1.25, 1.6, 95.35`). Material slot 1 is the screen; slot 0 and shared materials are untouched. The other TV under Lounge is not the media target.
- `PbnnScreen.shader` supplies an unlit, aspect-preserving image with black letterboxing. `ScreenAspect` defaults to 16:9. Screen UVs/framing need the live editor check below.
- The lower third is local to PBNN and created during playback: headline, source label and original bulletin date. It does not intercept mouse input. Its screen-space placement leaves the lowest 15% free for subtitles; adjust the authored anchors in `EnsureOverlay` if your broadcast layout needs more space.
- `PbnnMediaFixtureGeneration` is the last stage on the Default generator. It runs only for the explicitly marked media fixture, after dialogue/TTS/title generation, before Chat locks and saves. Ordinary generation is unchanged.
- `Fixtures/LocalImage.bytes` preserves the existing `Assets/Little Picture.jpg` as binary fixture input. Original image/material files and the user's screen layout were not edited. This image is labeled as a local illustration and not soccer footage.

## Saved data and playback

`Chat.GeneratedData["pbnn.media.v1"]` contains version 1, bulletin date, stable segment IDs, node ranges, display ID, headline, source ID/label/URL, origin, caption and image bytes encoded as base64. Embedded bytes intentionally make this small first slice independent of file paths, attachment URLs and downloads. The image is bounded to 2 MiB; runtime display accepts up to 4096 pixels per side. Larger media/file storage is future work.

Ranges are start-inclusive and end-exclusive. The media fixture's first node is an opening with no image. Middle nodes show the TV image and lower third. The final node begins a closing segment and clears both. No-media gaps, unknown nodes and invalid packets clear the prior content. Old episodes with no media packet play without media. There are no invented frame timestamps.

Playback uses shared ChatManager events with exact context/episode ownership checks. Node activation updates media before speech. Frame checks clear on skip, natural completion or loss of current playback ownership; context-change and disable handlers clear on channel exit and teardown. Runtime textures are released when cleared. Disabling the controller also restores the TV's original material array and releases its private material. Replay reads only the saved packet and does not run the generation stage again.

## Run in Unity

### TTS and loading recovery (2026-10-16)

The service-account key importer now uses RSA parameters rather than `ImportPkcs8PrivateKey`, which throws on Unity Mono. Validation includes 71 Google TTS regression checks and successful service-account RS256 signing/independent verification on Unity's bundled Mono, using a synthetic test key and no live Google calls. Recompile and retry the fixture with your existing credentials; no credential changes are required for this compatibility fix.

RemoteControl now cancels the outstanding intro fade before exit, preventing a late fade-in from restoring static. A 15-second channel-transition watchdog clears loading UI and warns if the readiness callback never arrives; this does not make an invalid channel ready. Intro-audio waiting is also bounded. Visible transition behavior still needs Play-mode verification.

### Existing newsroom staging now wired

The user's updated scene registers **Desk, Corner, Office, Lounge and Video Wall**. Their previously unassigned anchor/target/target-group and virtual-camera references are now repaired using the scene's existing camera rig. Desk uses Wide; Corner uses Two Shot; Office uses Close Up; Lounge uses OTS L; Video Wall uses OTS R. Only the Desk fixture has fixed framing proven geometrically; other locations remain starting points for later cast/blocking work.

Desk retains its two authored spawn transforms, made stationary. Its first spawn is named **Business Man 1**, at approximately `0.55, 0, 98.25`, so the current host takes that spot deterministically. Other locations currently expose their existing first spawn as stationary; unused child transforms are retained for future registration. The Video Wall spawn-group instance had inherited scale `480, 362.5, 150` and a -90-degree X rotation; those were normalized to unit scale/upright without editing the screen mesh or set layout.

The existing **Wide** camera is now the fixed newsroom fixture shot: position `4, 2, 103`, aimed toward `1, 1.25, 97`, vertical FOV 50. Its orbit/follow and aim components are disabled and Follow/LookAt references cleared. Desk has only this eligible shot, so speaker changes cannot randomly select another shot. Other existing cameras are retained.

Both fixture menu commands add a studio marker. The final fixture generation stage persists `Chat.Location = Desk` and the host's SpawnPoint name, regardless of model-selected staging. Older saved fixtures are not rewritten; generate a new fixture to test the updated saved location. `LocationManager` is not required for permanent studio sets; `ChatManagerContext.SpawnPoints` supplies location choices.

The helper below remains optional for creating alternative staging; it is not necessary to run the now-wired Desk fixture.

To bind the new set without guessing its blocking:

1. Outside Play mode, open PBNN and place an empty transform at the intended host's feet, oriented toward the audience/camera.
2. Author a Cinemachine virtual camera framing that spot and the desired TV. For the fixture, use a fixed shot without moving Follow/Aim behavior.
3. Open **HBOx → PBNN → Configure Fixture Staging**, assign that host transform and camera, and click **Create and register fixture staging**.
4. The helper adds `PBNN Fixture Staging` under the context with one stationary host spawn and the selected camera, then replaces the context's registered spawn group. It preserves old set objects and supports one-step Undo. Save the scene. To adjust afterward, edit the generated group's transform, Host spawn or camera reference.
5. Check `PbnnMediaPresentation.TvRenderer` targets the TV visible in that shot. The default media TV is the Desk TV identified above. Save, start Main, select PBNN, and queue the media fixture again.

This establishes a single new-set location. Four-wall routing, extra host positions and per-segment cameras remain the next staging slice.

1. Import/compile, then optionally run **HBOx → PBNN → Validate Media Scene** outside Play mode. This checks the actual scene references, screen submesh, shader errors, fixture decode and a real Chat JSON round-trip. It opens PBNN additively only if necessary and does not save the scene.
2. Start Main and select PBNN. If necessary install the existing fixture prompts using **HBOx → PBNN → Install Fixture Prompts**.
3. Run **HBOx → PBNN → Queue Media Fixture Bulletin**. It requires the presentation/generation components already wired in PBNN. Generation/TTS uses the normally configured providers. The original **Queue Fixture Bulletin** remains a media-free comparison.
4. Observe the targeted TV (identified above). On the middle speaking turns it should show the local image without stretching or recoloring the TV frame. The headline and source/date should appear together. The opening/closing must not show the previous segment's image or lower third.
5. Confirm the saved PBNN episode JSON has `pbnn.media.v1` under GeneratedData. Replay it through the existing replay flow and confirm the same image, attribution and boundaries without reading the original JPEG.
6. During the image segment, test skip and switching to polbots. Neither the image nor lower third may remain visible as current story content. Return and replay; the image must reappear at the intended boundary. Also play a media-free episode after the fixture to check stale-content cleanup.

Only the selected TV is connected. The other TV, board, curved video wall, image inset, ticker, full four-wall director, external image ingestion and video playback remain separate slices.

## Validation

- Runtime/editor C# compiled against Unity 6000.3.10f1 modules and the current runtime assembly: zero warnings/errors.
- `Tests/PBNN/Run.ps1`: 15 passing checks for the nested saved GeneratedData representation, exact image bytes/provenance, clear boundaries, old episodes, invalid versions, overlaps, unknown surfaces, missing attribution, bad base64 and oversized images.
- Scene static checks cover unique object IDs, controller/generator references, final generator ordering and fixture-byte equality.
- A headless Unity invocation exited with code 1 before running the validation method; its log did not provide a specific cause. Unity import/shader validation, actual Chat round-trip, visible framing and live cleanup remain unconfirmed until the editor checks above. The log is `Temp/PbnnMediaUnity.log`.
