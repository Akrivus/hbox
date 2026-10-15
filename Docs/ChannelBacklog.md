# HBOx Channel Backlog and Spec

HBOx will center its creative work on polbots, retain RomeBots, and replace AppyDays with PBNN (Polbots News Network), a newsroom using its existing assets. Soccer and globe can remain hybrid modes that use a host channel's cast and context. Each work session should produce one visible, independently reviewable result. The project name remains HBOx.

## Direction

HBOx should run continuously as a TV-like lineup of program blocks, supplying an inspiration board for polbots. Reddit-driven polbots remains part of the live stream; separately authored episodes can go to YouTube. Daily bulletins and recurring soccer events give the chaos some order. A program block selects what viewers see; a soccer recurrence creates an event inside polbots. These are separate responsibilities.

“Humanoid IPs” means RomeBots, SpaceDrivel, and the former AppyDays. Their systems need motivated decisions and watchable consequences rather than repeated catchphrases or narrative growth faster than the creator can follow. AppyDays is replaced by PBNN; its app-collaboration premise is no longer an active task.

Define the system before writing the story. A system supplies information, constraints, actions, and consequences that give the characters something to respond to. Reddit already supplies that role for the current shows; soccer adds live feedback and interruptions. New premises should establish a working loop before expanding the cast or commissioning more scripts.

Use the existing channel and mode vocabulary:

| Concept | Responsibility | Example |
| --- | --- | --- |
| Channel | Cast, context, prompts, configuration, identity | polbots, RomeBots, PBNN |
| Mode | A channel-local label whose matching components trigger behavior and wait for completion where needed | polbots topic patterns `Mode: Discord`, `Mode: Globe`, `Mode: Soccer` |
| System | Inputs, state, actions, and consequences characters respond to | Reddit posts, live match events, senate actions |

There is no shared portable mode contract. In polbots, a mode is currently a state selected by string matching and implemented by that channel's components, scene configuration, and callbacks. Hybrid means those behaviors coexist within the same channel context. It does not imply that soccer transfers to RomeBots or becomes gladiatorial combat. Modes and presentation overlap; keep the existing convention for this work. Globe is already implemented. Soccer already has match handoff and interrupts; first verify that implementation. Information produced by a system can feed another channel without transferring the simulation or its mode.

## Current implementation constraints

- `Assets/Main.unity` registers channels through `RemoteControl`. The shared `hbox.json` channels entry filters which ones preload; currently only polbots is enabled.
- Preloading loads each enabled scene and preserves its `ChatManagerContext` object and children. Returning to a channel reloads its scene and replaces its registered context, allowing the old generator to finish before retiring.
- Switching derives `Scenes/<context object name>` from `ChatManagerContext.ScenePath`. A new channel needs a matching scene name, unique context key, Main entry, build registration, and valid spawn points.
- Channel config overlays shared config. Prompts resolve under `Vault/<context key>/Prompts`; saved episodes use the context name as their folder in Documents.
- `ChatGenerator` accepts `Idea` objects directly. New sources can bypass Reddit pitch approval while retaining dialogue, TTS, saving, and playback.
- Generation queues completed chats into a shared playback queue. Any scheduled PBNN production must explicitly decide whether completion should trigger playback.
- Preserved registered contexts can continue generation while off-screen. Disabled channels do not preload. Channel switching and background generation need verification together.
- Old alternate scenes are reference snapshots for object/component trees. Use the current polbots scene and live soccer code as the baseline.

## Work session rules

The items below are sized as proposed afternoon sessions, not time guarantees. Each session ends at its completion check. If an item exposes a larger dependency, record and isolate that dependency rather than expanding the session into a channel rewrite.

Keep scene work, runtime changes, prompts, and source integrations in separate changes where practical. Use manual fixture inputs before live collection. Preserve working references and assets while deciding what to reuse.

## Soccer and globe

Implementation and remaining editor checks are tracked in [Soccer Implementation and Unity Handoff](SoccerUnityHandoff.md). User confirmation: **S1 complete; S2 complete; S3 configuration complete, awaiting one triggered recurring event; S4 report production demonstrated, PBNN consumption blocked until the PBNN channel exists.** Polbots memory recall remains a separate completion check.

Observed report: `c6bc1932bc604687ba737f9b83811788`, Scotland 0–0 Netherlands, completed 2026-10-07 at 18:11:44 Phoenix time. It contains participants, generated roster context, and twenty match event lines. SeriesId and ScheduledEventId are null, so this confirms report output from a match but does not confirm S3 dispatch. It contains no attributed reaction quotes or screenshot references.

Soccer remains coupled to polbots. Globe and soccer are polbots-local behaviors. The soccer integration uses Football Simulator team/player slots, polbots actor roster data and prompts, additive scene loading, and host UI/camera handoff. This backlog changes its presentation and event/reporting entry points; moving the simulation between channels or adapting it for gladiators is outside scope.

### S1 Confirm the existing soccer handoff

Trace the current `Mode: Soccer` entry, additive match loading, context binding, camera ownership, interrupt injection, and exit. Compare the existing call-to-globe transition to identify reusable UI behavior. Record which pieces are already complete and the exact camera/UI changes required for S2.

Use the current scene and runtime. The old Soccer Mode scene demonstrates a preserved coupled setup, but is an object-tree reference rather than the implementation baseline. Do not create a second polbots context to represent the match.

Completion check: confirm mode entry, match feedback, one interruption, and return to call/globe behavior in Unity. Record failures as bounded follow-up work. The confirmation sweep itself does not require a presentation rewrite.

### S2 Full screen match and talking heads

Depends on S1. Decouple the football camera from the media-share render texture and use it as the full-screen background. Reuse existing head UI as a right-side vertical stack of three or four heads. Keep the central action area clear. Use the existing mode transition approach and restore the prior layout/camera on exit. Inspect watchdog and audio listener behavior so they do not restore the old render-texture path.

Completion check: match play fills the background, three or four heads remain readable at the right, goal interruptions still work, and mode exit restores the prior view without stranded scenes or duplicate listeners. Dynamic visibility is a follow-up only if this fixed layout needs it.

### S3 Scheduled matches as events

The main option is an ongoing rolling schedule: choose random participants from the current polbots cast for each new fixture, persist the pending matchup, and carry past winners/results plus the current discussion into generation. Avoid immediate rematches when alternatives exist. Remembering winners does not require them to play every future game; winner-stays is optional. The Schedules array is the only scheduling configuration; each entry has independent recurrence and history. Explicit dialogue requests can still supply Home/Away. See the Unity handoff for configuration.

Define a minimal event record with match ID, host channel key, participants, scheduled time, and status. Prefer polbots owning the first schedule; an external scheduler can later proxy the same event through polbots. Dispatch one due event into the existing soccer idea/mode path. Specify what happens when polbots is off-screen: proposed first policy is to keep the event pending until polbots is viewed, without forcing a channel switch. Whether matches must run unattended at the exact scheduled time remains an explicit later decision.

Completion check: one scheduled fixture enters soccer once through polbots, retains its match ID through completion, and does not start twice after a mode transition. Each entry in the Schedules array persists its next occurrence and accepts interval or weekday/local-time recurrence patterns; see SoccerUnityHandoff.md for configuration.

### S4 Match reports shared with memory and PBNN

Status: report export demonstrated by the Scotland–Netherlands fixture above. PBNN playback/ingestion is blocked by N1. Do not hold report capture improvements hostage to channel creation.

Depends on a working match lifecycle; PBNN consumption also needs N2. Export one structured completed-match report containing match ID, participants, final score, timestamp, key events, and host context. Feed the report into polbots continuity/memory and expose it for a PBNN generator to pick up. Reuse existing soccer continuity where it already covers this; avoid independently inventing a second result.

Completion check: a later polbots scene recalls the actual result and a fixture PBNN sports segment reports that same match. Track consumed report IDs to avoid duplicate bulletin items. PBNN may send a later reaction or interview idea back through the normal polbots generator; it does not own the game simulation.

## PBNN

Implementation inventory, existing-code limits, fixtures and thread handoff: [PBNN implementation guide](PBNNImplementationGuide.md).

PBNN is a news bulletin for the HBOx universe, led by Paul Bott. Soccer results, RomeBots story developments, and polbots scenes can supply material alongside outside news sources. Each producer owns its story/simulation; PBNN compiles and presents their emitted reports. Reuse suitable AppyDays staging and assets. Daily output should be roughly 3–15 minutes, determined by available information without padding to fifteen minutes. The presentation looks serious and is informative without trying to compete with local TV news. PBNN is an informational broadcast rather than another country-character discussion show. The first episode presents a small compiled briefing; animation, music, and responsive graphics make it feel like a broadcast. Guest polbots and a substitute host follow after that loop works.

### N1 One broadcast from a fixture

Create the PBNN scene/context, config, Main entry, build registration, and minimal prompts. Reuse a desk, set, and host assets where appropriate. Feed one manually curated briefing directly into a generator without a Reddit pitch gate.

Completion check: select PBNN, generate and play one short bulletin, save it, and replay it with the correct host, scene, and context. Leave enabled-channel configuration explicit.

Session boundary: begin by duplicating suitable AppyDays scene wiring and changing identity/config/prompts. Use placeholder host visuals. Custom host art and broadcast graphics belong to N6.

### N2 Preserve the source packet

Depends on N1. Attach a structured packet to the saved episode: stable source IDs, titles, URLs, publishers, publication times when available, retrieval times, factual notes or excerpts, and the bulletin coverage window. Link each story to its source IDs. Keep source material separate from generated performance text.

Choose a typed episode field or a namespaced payload in `Chat.GeneratedData` after checking save/load compatibility. `Idea.Source` alone is not enough for multiple articles.

Completion check: save and reload a fixture bulletin; every story still resolves to its original sources and dates.

Session boundary: one payload and one round-trip fixture. Feed ingestion and web publishing are separate tasks. Allow both article sources and HBOx match reports as packet items.

### N2b Ingest HBOx story reports

Start with existing soccer reports as the first producer adapter. Preserve their authoritative scores and stable match IDs. Define a shared report envelope when adding the next producer: report ID, originating channel, episode/event ID, occurrence timestamp, summary, participants, source references, and a payload for producer-specific details. RomeBots can emit political decisions and crises; SpaceDrivel can emit ship incidents, repairs, and mission progress; polbots can emit notable story changes. Upcoming episodes and scheduled events can also supply announcements, distinguished from completed results. Preserve the distinction between fictional in-universe developments and sourced real-world claims.

PBNN ingestion records consumed report IDs and compiles a coverage window into N2 source packets. Keep original producer reports accessible so anchors' claims can be traced back. Replaying a bulletin must not consume reports again. Soccer can grow its own adapter for results, history, and follow-up interviews without making PBNN responsible for simulation.

Completion check: ingest a soccer report and one fixture RomeBots or polbots story report into the same bulletin packet; retain their origins, reload the packet, and collect again without duplicate items. Implement producer hooks individually after the envelope and first adapter work. No general story-report pipeline is implemented yet.

### N2c Reaction quotes and firsthand screenshots

An optional enrichment step for producer reports, independently buildable before PBNN exists. Begin with soccer, retaining the original match report as the authoritative result. Add attributed reaction excerpts from the dialogue actually played: speaker, exact text, match/report ID, source chat/interrupt ID, and playback/event timing. Capture after playback rather than quoting a generated reaction that never aired. Commentary is an attributed character reaction, not evidence that its claimed event happened. The current KeyEvents lines are event summaries, not attributed dialogue.

For visuals, capture a small bounded number of screenshots from the actual match presentation on selected events (for example kickoff, a goal, final whistle). Store image assets and persist relative references, capture time, match ID, associated event, and caption with the report enrichment. Save/load must preserve access to those assets. A simple manual screenshot fixture can establish the payload before automatic capture is wired. Select one image for the broadcast's corner inset; rendering it belongs to N6.

Use firsthand images when reporting the HBOx event. AI reconstructions may be used for comedy, visibly identified as reconstructions so they do not appear to be captured footage. For real-world stories, keep factual reporting tied to preserved sources; the anchor can be unreliable or theatrical without silently inventing facts or presenting fabricated visuals as evidence. No blanket accuracy constraint is imposed on fictional character reactions.

Completion check: attach one exact played reaction and one real screenshot to a report, reload it, and resolve both to their original match and source. Later show the screenshot inset and attributed quote in a PBNN fixture bulletin. Automatic event capture, quote selection, and broadcast rendering can be separate afternoon tasks. This enrichment is specified, not implemented.

### N3 Bulletin replay configuration

Use the existing recent-first replay behavior. `MaxReplayAgeInMinutes` prefers recent episodes and falls back to all episodes when none are recent; that fallback is useful for PBNN. `ReplayRate` records remembered replay history, rather than playback frequency or expiration.

Start with a 24-hour preferred age, a larger remembered history, and a small replay batch. Let today's bulletins be rewatched, then allow older bulletins when current material is exhausted. Show the bulletin's original date so an older replay is recognizable. Confirm the actual ordering: current code falls back to old material only when no recent candidates exist, so exhaustion of today's selection may need a small ordering change if it otherwise repeats today's list forever.

Completion check: fixtures from today and yesterday establish the replay order, today's news can be rewatched, and older material remains playable. A completely empty library uses a simple waiting state. No strict expiration feature is required.

Session boundary: configuration and a manual replay sweep first; change selection code only for an observed mismatch with this policy.

### N4a Accept submitted screenshots

A household member can submit screenshots of local posts, including Facebook-group material, for a later or next-morning bulletin. Choose one intake route, such as an existing Discord attachment handler or a watched folder. Preserve the original image and available location, date, link, and submitter notes.

Extract visible text into an N2 source packet. Separate that text from inferred context; a screenshot does not automatically confirm an incident. Do not invent missing facts. This acquisition route is distinct from N2c’s firsthand captures of HBOx events.

Completion check: submit one screenshot, preserve its image and provenance after reload, and generate one segment within the supplied information. Intake, text extraction, and segment generation can be separate afternoon tasks.

### N4 Collect one information feed

Depends on N2. Implement one feed adapter with deduplication, publication filtering, retrieval timestamps, and a bounded story count. Research one usable text source before choosing an adapter: local coverage, available text, access method, retrieval/storage permission, and cost. A headline/link aggregator alone may not supply enough context. Prefer an available feed or API where practical; scraping is not a settled choice. Reddit can supply topics or reactions; preserve the underlying reporting links separately when available.

Completion check: collect twice from the same fixture or feed, avoid duplicate stories, and generate one source-linked briefing. Broad source discovery, local coverage, and editorial weighting are separate research work.

Session boundary: one adapter producing N2 packets. No multi-source ranking, crawling, or full news research in this implementation task.

### N5 Scheduled bulletin production

Depends on N3 and at least one input path (N2b, N4a, or N4). Produce a daily briefing, potentially next morning from the prior day’s submissions, at configurable recurring times with a coverage window, deduplication across runs, and overlap prevention. Separate generating and saving a bulletin from immediately queuing it for playback. Support an explicit generate-now action.

Completion check: trigger two shortened intervals, including one while another channel is on screen. Bulletins save without unexpectedly changing the viewed channel; PBNN later plays them under N3's replay policy.

Production disposition is implemented in `ChatGenerator` (Inspector `Queue For Playback`, default on; explicit `GenerationDisposition.SaveOnly`). Episode persistence is awaited and incoming-file sweeps respect the saved disposition. See `Docs/GenerationDisposition.md` for usage, regression coverage, and outstanding Unity checks. Recurring triggers and live generation/save/replay validation remain outstanding.

Session boundary: first add a generation disposition that saves without queuing, then wire a simple interval trigger. Treat these as two small changes if the shared playback behavior needs broader work. Reuse existing batch scheduling only where its coupling permits.

### N6 Broadcast graphics and sound

Depends on N1; can proceed before live ingestion. The eventual studio may have four presenter stations arranged as a plus sign, with the camera turning toward the station responsible for each segment and UI transitions bridging those shifts. Start with two stations and one transition before constructing all four. Build one complete segment transition: opening sting, host introduction, lower third, story change, and closing cue. Drive graphics from segment/speech state. Keep music below speech and restore UI/audio state on channel exit.

Completion check: one bulletin looks and sounds like a broadcast through its full sequence, including skip and channel-switch behavior. Advanced animation follows separately.

Work as three independently visible passes: lower thirds/story state, music cues, then host/transition animation. Each pass uses the same fixture bulletin and may be its own afternoon.

### N7 Public source access

Depends on N2. Add an episode source endpoint/page to the existing web service. First deliver a readable page showing the bulletin date, stories, and source links. Then add its URL to Discord episode messages. Add a QR overlay as a separate small task once the page works on a phone.

Completion check: the same stable episode URL resolves during original playback and replay. The phone can reach the chosen web-service address; a QR code must not assume the desktop's local address is accessible to viewers.

Work as three small changes: source endpoint/page, Discord link, then QR overlay. The endpoint serves the saved N2 packet; it does not recollect sources on every request.

### N8 Television delivery

Choose a viewing path that avoids an office-to-TV cable: compare a local stream/player with a hosted stream available through the TV’s existing apps, including YouTube. Delivery remains undecided; verify the household device’s capabilities before selecting a protocol or service.

Completion check: watch one fixture bulletin on the household TV with working audio and repeatable access. Continuous streaming, latency tuning, and hosting costs follow separately.

### N9 One recurring program block

Audit existing scheduling/channel selection first. Define one block with start time, duration or end condition, selected channel, and return behavior. Keep production separate from channel selection: generating a bulletin should not switch the viewed channel, and a PBNN block can play saved material. Reuse recurrence vocabulary where appropriate without turning the soccer dispatcher into a universal scheduler.

Completion check: transition from polbots to a saved bulletin at a shortened test boundary and return afterward. Resolve behavior for an active match and an empty PBNN library before unattended use. Expand to a full-day lineup only after this slice works.

Later experiments: guest polbots and substitute-host explanations. Reports from other HBOx IPs are a core input path.

## RomeBots republic and show modes

RomeBots starts as a clean-slate res publica rather than recreating Augustus or a fixed historical narrative. Choose its period and cast later. A persistent political system should constrain narrative growth. Whether an actor respects the republic or escalates into a Sulla-like seizure of power is an open dramatic question, not a predetermined plot.

### R0 One republican decision

Define a small founding rule set (roughly twelve rules is an initial idea, not a fixed count), a bounded cast, offices/powers, and durable state. Choose actual rules before expanding prompts. Give actors goals and validated actions; distinguish authoritative consequences from their interpretations. Proposals, votes, grants of power, and violations should change observable state.

Completion check: one fixture decision changes stored state; a second short scene responds to its consequences and actor goals. Reject invalid actions without inventing state changes. Emit one story report for PBNN. Full constitutional design, elections, and civil-war simulation remain later work.


Keep RomeBots political without requiring historical recreation. Each mode supplies a situation and actions; scripts respond to that system. Gemini TTS is the intended delivery option for escalating performances.

### R1 Senate action loop

Use the existing `ChatNode.Actions` spine: action text is extracted separately from spoken dialogue. Add a scene object responding to node cues, with a small mapping for interrupt, stab the speaker, and stab self. Start with one action and placeholder animation. Resolve actor targets from current scene state and define what a defeated actor can still do.

Completion check: an action embedded in a fixture debate triggers visible behavior while remaining absent from spoken dialogue; invalid targets fail cleanly. Then confirm the existing dialogue generator can emit that same action. New tool-call infrastructure is optional future work.

The same cue mechanism can serve polbots soundboard actions such as eating chips or making noises. Verify cue timing and dispatch before adding a large action library.

### R2 Gladiator physical prototype

Independently prototype two active ragdolls in a pit, one attack, one hit response, and reset. Send hit events to a small performance adapter. Physical outcomes are authoritative; the generator describes and reacts to them.

Completion check: repeated attacks produce observable hits and recoverable state. This is a physics spike and may expose work larger than one afternoon; dialogue expansion waits for the physical loop.

### R3 Choose the reactive generation boundary

Depends on R1 or a working R2. Existing dialogue and TTS are generated before playback. Scripted action cues can execute during that playback, but reacting vocally to an unscripted hit requires a later generation opportunity. Do not assume already rendered speech can change its delivery live.

Compare three approaches: fully scripted escalation, short sequential scenes generated from updated state, and soccer-style interrupt packets during a longer scene. Proposed first experiment: generate one short scene, execute its actions, snapshot the resulting state, and generate a second short scene that responds. Keep it independent of Reddit pitching. Evaluate interrupt reuse afterward; a new continuous generation mode is a larger architectural choice.

Completion check: two short scenes show one real state change carried into the next generation and its Gemini TTS performance. Record generation latency, continuity, and whether interrupt packets would improve the result. This task selects an architecture; it is not just an anger component.

Family dinner is a later setting for the same proven action loop.

## SpaceDrivel goals and ship state

AppyDays is replaced by PBNN; preserve useful assets for the newsroom. SpaceDrivel’s ship owns persistent state and problems. Characters pursue goals with incomplete information and uncertain action outcomes, like an observed role-playing game. Bounded worldbuilding, resources, and available actions should motivate choices rather than plot demands or repeated catchphrases.

### D1 One repair with an uncertain outcome

Create one ship fault, a diagnostic view, a repair action, and a small dice-roll/result table that changes authoritative state. One character can see diagnostics while another operates the tool. Give each a concrete goal. Generate a short scene, resolve the repair attempt, then generate a reaction from updated state; dialogue alone cannot declare success.

Completion check: the same attempted action can produce distinct recorded outcomes, characters adapt to the actual result, state survives reload, and a PBNN report describes the incident. Begin with placeholders and a small world-state packet. Larger campaigns wait until this loop is watchable.

Dogfight remains optional later work: a bounded encounter controller owns outcomes and stage directions drive animation. Ship repair takes priority; D1 needs no space-combat simulation.

## Suggested starting order

1. Trigger one S3 recurring event and confirm its event/series IDs; S1 and S2 are complete.
2. N1 for a playable PBNN channel, then N2 and N3 for sources and replay.
3. N2b using the existing soccer report; N2c enriches it with reactions and screenshots.
4. N6 for broadcast presentation, starting with two stations and one camera transition.
5. N4a screenshot submissions or N4 one researched text feed, then N5 daily production.
6. N7 source access, N8 household TV viewing, and N9 one recurring program block.
7. R0 republican state, R1 visible actions, and R3 reactive generation; D1 is a separate SpaceDrivel experiment.

Choose one item per session. Full-day programming, broad source research, universal mode portability, and active-ragdoll polish remain separate projects until a working slice justifies them.
