# PBNN studio roadmap — 2026-10-17

## Direction reset — 2026-10-18

The experimental saved rundown/director, separate Studio Rundown generator, placeholder multi-cast prompts and desk-to-lounge scene changes have been removed. Retain the working single-host/media fixture and develop the studio first. The distinction between a segment, a scene and a Chat is unresolved; neither one-Chat-per-bulletin nor a separate generation workflow is a requirement. Revisit transitions only when a concrete studio interaction needs them.

## Working baseline and audit

The user reports a successfully generated/played fixture, with a promising result but an incorrect camera angle. Keep that as the working baseline. Replay consistency, skip cleanup and channel-return cleanup still need explicit confirmation. Do not treat static or serialization checks as visual verification.

The current scene registers all five defined interior locations through `ChatManagerContext.SpawnPoints`. Its `Locations` list uses the same names. Each group has the existing camera anchor, target and target-group references plus one non-null virtual camera. The fixture persists `Desk`, the Business Man 1 host spawn and its media cue ranges. The studio floor has no registered program location and stays postponed.

| Scene location ID | Design label | Registered spawn slots | Existing shot | Current qualification |
| --- | --- | --- | --- | --- |
| Desk | Anchor desk | 2; first named Business Man 1 | Wide, fixed fixture view | Working fixture; user will refine angle |
| Lounge | Conversation lounge | 1 | OTS L | Registered; needs multiple conversation seats and framing |
| Office | Production office | 1 | Close Up | Registered; needs production-team blocking and dynamics |
| Corner | Presentation corner | 1 | Two Shot | Registered; needs presenter/questioner positions and board focus |
| Video Wall | Big picture room | 1 | OTS R | Registered; needs visible-region/focus planning |

Keep these scene IDs stable while using the design labels in documentation/UI. A registered location is available to the episode-level staging system; it does not yet support segment-by-segment switching within a bulletin. Shared camera-target wiring is not a portable mode framework.

The cast inventory contains 60 actors with identities, prefab references, pronouns and audition voices. Only Business Man 1 is currently in the scene's active cast. Do not add the entire inventory to generation automatically. Final anchors and production crew are separate fixed ensembles; guests/presenters can be selected for the other formats.

## Prompt audit

All six tracked templates under `Docs/PBNN/Prompts` exist in `Vault/pbnn/Prompts` and match byte-for-byte at this audit. Runtime prompt routing uses the **context key** `pbnn`, independently of the scene object name `PBNN`.

| Generation step | Runtime prompt or behavior |
| --- | --- |
| Scenario/cast selection | `Defaults.md`; Default.md is absent, so the normal fallback applies |
| Host profile | `Actors/Business Man 1.md` |
| Behavior | `Defaults/Behavior Generation.md` |
| Editor process | `infer: 0`; builds context from the rundown/host profile without another prompt request |
| Dialogue | `Defaults/Dialogue Generation.md` |
| Sentiment/delivery | `Defaults/Sentiment Analyzer.md`; preserves spoken wording |
| Speech | Actor's Gemini voice/direction/config; no Markdown TTS prompt is needed |
| Title/synopsis | `Defaults/Title Card Generation.md` |
| Media/staging fixture | Explicit fixture markers; final generation stage saves Desk and media data, without an LLM prompt |

The current templates deliberately constrain output to Business Man 1 and a short soccer bulletin. The host profile is generic delivery direction, not a developed personality. There is no missing fixture prompt explaining that flatness. The remaining inventory does not yet have individual performance profiles. Merely registering another actor will not produce a good duo while these templates still mandate one speaker.

Retain the current fixture as a regression path when adding formats. Before registering new cast, author matching `Actors/<Name>.md` profiles. Add format-specific scenario, behavior and dialogue prompts so the office/conversation formats do not inherit a single-anchor factual bulletin script. Preserve source-linked claims where a format uses real reporting, while allowing fictional crew interactions and character reactions.

## Design each room around its loop

| Space | Input → performance → result | Cast and staging needs | Media/focus needs |
| --- | --- | --- | --- |
| Anchor desk | Approved news packet → short scripted bulletin and handoff → next story or return later | Fixed anchor ensemble, stable desk positions, intentional single/two-shot choices | Behind-desk UI, story image, headline/source/date; clear ownership between physical display and broadcast overlay |
| Conversation lounge | Bounded list of facts or topic → hosts/guests discuss, misunderstand and respond → useful takeaway | Recurring facilitators plus guests as appropriate; readable group layout and reaction shots | Optional supporting image/list; conversation should still work without a screen |
| Production office | Concrete production problem → crew tactics and conflict → a decision, changed obligation or consequence | Fixed production crew with user-authored relationships; workplace blocking | Optional monitor/status information; primary focus is people and their working problem |
| Presentation corner | Explanation outline plus graphic → presenter explains while anchors ask questions → clarified concept | Presenter plus questioners; specify whether questioners are physically present or handled through saved cutaways | Board image/page, selected region or marker, presenter focus, then questioner reaction; start with authored poses |
| Big picture room | Image/map or ordered visual panels → presenter inspects several points → interpretation or handoff | Presenter/guests chosen for the subject; walkable visible regions planned with cameras | Curved-wall regions, crop/panel choices and saved focus changes; source image need not fill the whole wall |

The five formats can occupy the existing physical studio without requiring five walls. The floor, final dressing and responsive graphics can follow these working loops. An Electoral College explanation is a useful presentation fixture; a short factual list is a lounge fixture. Neither fixture requires live ingestion. Production-office scripting should wait for a concrete crew dynamic rather than generating generic banter.

## Next studio work

1. **Refine the working desk.** Adjust framing, host placement, the TV/behind-desk UI and the lower third. Confirm the existing fixture can generate, play, replay, skip and return after a channel switch.
2. **Finish the other set layouts.** Author usable positions and camera views for the lounge, production office, presentation corner and big picture room. Register and validate their references. Keep the studio floor postponed. Do not require a generated multi-room program to evaluate a set.
3. **Exercise displays independently.** Establish the board's readable area, the TV framing and useful regions of the video wall with local images. Extend the existing media handling as actual surface requirements become clear.
4. **Explore casting in the finished spaces.** Use existing actor assets as stand-ins for placement and camera work. Choose anchors, production crew and guests, then develop their dynamics and performance profiles. Keep temporary casting out of production generation code.
5. **Define the content/playback boundaries from evidence.** Decide what constitutes a scene and a segment, what Chat should represent, and whether moving between parts of the studio needs any separate workflow. Only then implement the smallest data-driven solution needed by a real interaction. The room loops above are design intentions, not an imposed rundown schema.

Source ingestion, scheduling, staffing/rotation, transition orchestration and advanced walking/IK remain later work. The existing media fixture is a bounded test, not a required structure for all PBNN content.
