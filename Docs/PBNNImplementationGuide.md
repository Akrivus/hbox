# PBNN implementation guide

PBNN (Polbots News Network) replaces AppyDays as HBOx’s informational broadcast. Preserve the working single-host/media fixture while finishing the physical studio and its presentation capabilities. Ingestion and orchestration follow demonstrated studio needs. This is an implementation inventory and handoff, not a commitment to build every brainstorm. See [Channel backlog](ChannelBacklog.md) for the wider direction and completed soccer work.

Current status (2026-10-18 direction; Studio Preview available): the user has successfully played a generated test fixture. Camera angle still needs refinement; replay, skip and channel-return checks are not yet explicitly confirmed. The channel shell, 60 actor/prefab identities with provisional Gemini voices, and single-host prompts are present. The full source/rundown contract and segment director remain unimplemented. See [PBNN Unity handoff](PBNNUnityHandoff.md) for the original implementation history and [current studio roadmap](PBNNStudioRoadmap.md) for the five spaces, prompt audit and next slices.

Media slice status (2026-10-16): one TV and a local headline/source/date overlay are now wired to a versioned saved `pbnn.media.v1` fixture payload, with segment, skip and channel-exit cleanup. This is a bounded media slice of P2/P4, not the full source/rundown contract or four-wall director. See [PBNN media handoff](PBNNMediaHandoff.md) for replay semantics, tests and outstanding Unity verification.


## Direction reset — 2026-10-18

The experimental saved rundown/director and separate Studio Rundown generator, placeholder multi-cast prompts and desk-to-lounge transitions were removed. Preserve the working single-host/media fixture while completing the studio, its registered locations, camera views, actor placement and displays. Segment versus scene versus Chat remains undecided; the earlier one-Chat-per-bulletin proposal is not a current requirement. Multi-room generation and transition orchestration are deferred until an actual studio interaction demonstrates the need. The source/rundown contracts below remain design proposals, not implementation commitments. Follow the [studio roadmap](PBNNStudioRoadmap.md) for current priorities.

## Retained fixture baseline

Keep the working single-host desk/media fixture for checking the studio's cameras, placement, local image, headline/source overlay and replay cleanup. Its node-based image cues are a bounded test, not a definition of scene or segment boundaries. Finish and inspect the other sets independently; a generated multi-room performance is not required to validate their layouts. Live ingestion, anchor rotation, weather maps, IK and field reports do not block studio work.

Daily bulletins may run roughly 3–15 minutes when the material supports it. No padding requirement. Paul Botz is one possible anchor, not the only host. Models/assets are available, but casting and personalities need user development. Here “model” means a character model; do not assume each anchor requires a different LLM.

## Existing code and limits

| Area | Existing entry points | Reuse and remaining work |
| --- | --- | --- |
| Channel registration | `Assets/Main.unity`, `ChatManagerContext`, `RemoteControl`, `ProjectSettings/EditorBuildSettings.asset` | Implemented pbnn context, Main/build registration, config and fixture prompts. Preserve AppyDays as historical reference; validate the current PBNN scene. |
| Humanoid assets | `Assets/Scenes/AppyDays.unity`, `Assets/Scenes/AppyDays/Actors`, `Assets/Scenes/AppyDays/Prefabs/Actors` | Current PBNN inventory has 60 actor definitions and prefab references; Business Man 1 is the only active fixture host. Original copied-office/Walt notes in the Unity handoff are historical. |
| Generation/save | `Assets/Core/ChatGenerator.cs`, `Assets/Core/DataModels/Chat.cs` | Direct Idea queue already bypasses Reddit acquisition. GeneratedData can carry a namespaced serialized packet after round-trip validation. Normal generation saves and queues playback; inspect the current Generate routine before adding background production. |
| Locations | `Assets/Core/Integrations/Location/LocationManager.cs`, `LocationDefinition.cs` | Chat.Location loads a location prefab on queue-take and assigns actor spawn points. This is episode-level staging, not an existing segment-by-segment director. |
| Cameras | `Assets/Core/SpawnPointManager.cs` | Virtual-camera array exists; node activation resets its cameras' priorities and samples a camera. PBNN uses predefined fixed virtual shots; each of the five registered locations currently has one eligible shot. Registration supports episode-level staging, not segment switching. Verify Cinemachine selection and framing with the actual scene wiring. |
| Playback cues | ChatNode.Actions, ChatManagerContext.OnChatNodeActivated | Existing media fixture handles node activation with ownership checks. Any future cue routing requires a concrete interaction and timing/lifecycle review; do not add a director now. |
| Pitch review | `Assets/Core/Integrations/Sources/RedditPitchCandidateService.cs`, `RedditPitchGate.cs`, `RedditSource.cs` | GenerateAsync evaluates the pitch and returns null on rejection. Source then auto-queues accepted candidates or posts for voting. Reuse the flow; its source model, prompts, store names and converter are Reddit-specific. |
| Discord | `Assets/Core/Integrations/DiscordBotService.cs`, `DiscordParticipationPolicy.cs` | Gateway handles reactions/interactions and vote integration. Dispatch currently has no MESSAGE_CREATE intake. Tips text/attachment acquisition is new work. |
| Vote/config controls | `Assets/Core/Integrations/Config/RedditConfigs.cs` | Existing batch, auto-approval count, minimum votes, score, expiry, and active-window settings are references. Enlarging batch size alone does not define bulletin segment voting. |
| HBOx reports | SoccerEventStore, ServerSource `/api/soccer/reports?limit=50` | Soccer emits real match results. Use as first report adapter; RomeBots and polbots story-report hooks remain future work. |
| Shared UI policy | `Assets/Core/UI/UiVisibilityCoordinator.cs`, `UiVisibilityPolicy.cs` | Keep global visibility rules here. PBNN’s ticker, image panel and segment transitions belong to its local presentation controller; avoid scattered global show/hide rules. |

LocationManager needs a focused lifecycle check before reuse: it assumes a nonempty Locations array and destroys the previous LocationDefinition component rather than explicitly removing the instantiated root. Confirm prefab ownership and cleanup; do not stack old sets by accident. Context event wrappers forward shared ChatManager events, so PBNN listeners must check current channel/episode ownership and unsubscribe on teardown.

## Historical architecture proposals — deferred

The following data-flow, saved-record and presentation-cue ideas are retained for reference, not active requirements or an agreed segment contract. Do not implement `pbnn.rundown`, saved camera cues or a separate generator from this inventory. Revisit only after the studio demonstrates an interaction that needs orchestration.

### Proposed data flow

Tips, articles, Reddit material, weather, and HBOx reports feed independent adapters. Each adapter preserves originals and produces a common source packet. Review accepts or rejects a candidate. Accepted candidates either enter the bulletin pool immediately or wait for a vote. A compiler selects stories and produces a rundown; generation writes performance text against that rundown. Playback consumes saved segment cues and media references.

Auto-approval means skipping the audience vote after review accepts a candidate. It does not bypass review and is not proof a factual claim is true. Keep rejected candidates out of the production pool. Voting chooses inclusion/priority, not factual accuracy. Trusted in-universe match results can use a simpler editorial suitability check than unverified submitted claims.

### Proposed saved records (not a contract)

| Record | Required information |
| --- | --- |
| Source packet | ID, kind, origin/author, source URL/message/report ID, publication time if known, retrieval/submission time, location if relevant, preserved text, attachment references, factual notes and attributed claims |
| Candidate | ID, source IDs, segment proposal, review result/reason, status, vote binding and expiry when applicable |
| Rundown | Bulletin ID/date/coverage window, selected anchor IDs, ordered segments, source IDs and media references |
| Segment | ID/type, set ID, fixed camera ID, speakers, script/node boundary, headline/lower third, image/map reference, caption, cue list |
| Media asset | ID, stored relative path, type, origin/capture time, attribution, caption, optional source ID and map markers |

These are proposed contracts, not existing types. The earlier versioned `pbnn.rundown` payload in GeneratedData was a proposal; it is not the next implementation step. Keep originals separate from generated performance text. Verify media paths after save/reload; downloading an attachment without persisting its link to the story is insufficient. Replays must not ingest or consume the same items again.

Awaiting saves is not a prerequisite. For live production, a save-success event can commit a recoverable reservation after the file write completes. The current `Chat.Save()` is `async void` and exposes no success notification, so generation completion alone must not commit that reservation. Alternatively, poll known producer report routes, durably record reports by stable ID, and pick up new reports on the next collection pass. Track collected report IDs separately from inclusion in a saved bulletin: acquisition deduplication does not mean a story has aired or been consumed. Implement bulletin inclusion tracking only when live compilation needs it; saved fixtures do not require a reservation system.

### Historical set/camera proposals

A cross-shaped studio is optional. Isolated cubicles or disconnected prefab sets can provide the same views; physical adjacency is not a broadcast requirement. Use a fixed number of predefined shots at first. Choose one rendered camera with fixed virtual shots where existing Cinemachine wiring fits, or a controlled camera switch if required; assign one owner and keep one effective audio listener.

| Segment | Initial staging | Later options |
| --- | --- | --- |
| Headlines | Anchor/desk, headline UI, corner image | More anchors, full-screen image or b-roll |
| Weather / internet weather | Presenter beside image panel | Map markers, pointing targets, IK |
| Interview | Two chairs and fixed two-shot | Interview cutaways and remote testimony |
| Desk conversation | Two anchors at a desk | Reactions and handoffs |
| Special / field report | Separate location prefab and fixed shot | Scenic backdrop, foreground props, passing cars and environmental sound |

The earlier one-Chat-per-bulletin design is deferred. First complete usable permanent sets and scene-level placement/camera wiring. Determine the relationship between Chat, scene and segment before introducing a saved director or separate multi-room generation workflow.

The superseded cubicle proposal used stable anchor positions, two permanently available shots and saved segment/speaker boundaries for automatic cutaways. It is retained as an architectural option only; it is not the current five-room workflow or a requirement to build two-set routing.

Prefer fixed virtual-camera configuration over suspending the existing speaker behavior. A single eligible camera in the active SpawnPointManager makes its per-node selection deterministic; a broadcast camera outside its managed array can instead retain a higher priority. Choose the smallest approach that works with the scene's active managers and Cinemachine brain. A fixed transform alone does not prevent another virtual camera from winning priority, and existing target-follow components must not move the authored shot. Verify both selection and framing across consecutive speakers and cutaways.

### Historical presentation states and cue proposal (deferred)

Proposed local states: opening, transition, segment, closing, idle. A segment selects its set/camera, clears prior media, applies headline/lower-third state, and shows its image when cued. Transitions can play a swoop/sting and a generic station voice; persist the chosen cue so replay stays consistent. Ticker text comes from saved approved headlines/announcements, with the bulletin date visible on older replays.

Use stable segment IDs and node-boundary cues initially, rather than invented frame-accurate timestamps. Clear UI/audio and restore camera ownership on skip, replay start, channel change, or interrupted transition. Unknown set/camera/image IDs use a defined default or omit the image; they must not leave the previous story’s picture showing. Keep full-screen video and streaming footage for later.

`OnChatNodeActivated` fires before `ActorController.Activate`, making it suitable for immediate camera and graphic cues. It is a synchronous event, so it cannot by itself hold speech until a transition sting finishes. Initially use immediate cuts; add an explicit coroutine playback boundary only when a transition needs to delay speech.

## Deferred ingestion ideas

These are later adapter considerations, not studio-completion prerequisites.

### Discord tips hotline

One configured channel acts as a dropbox for household members and friends: text, links, screenshots and attachments. Inventory existing bot intents/permissions and participation policy before enabling message intake. Add MESSAGE_CREATE routing scoped to the configured channel, ignore bot/self loops, and marshal Unity work to the normal main-thread queue. Persist Discord message/channel IDs for deduplication, author/submission time, original text and stored attachment references.

Separate acquisition from pitch generation/review. Adapt source-neutral packets into the existing review/voting flow without labeling submissions as Reddit posts. Start text-only, then add attachment capture and text extraction. Optional votes should target story candidates within a bulletin pool; do not accidentally generate an entire episode for each accepted tip unless that is the chosen first slice.

Completion sequence: submit a text fixture twice and create one candidate; reject one fixture and verify it never queues; accept one fixture with automatic elevation; accept another for a vote and elevate exactly once; finally reload one screenshot submission and resolve its stored image. Review and auto-approval must be separately configurable. More batch capacity is a later tuning change.

### HBOx universe reports

Start with soccer’s completed report and its stable MatchId. The supplied Scotland–Netherlands 0–0 report is a useful fixture. Preserve authoritative score and distinguish event-summary lines from attributed reactions. Add played dialogue quotes and screenshots as separately linked evidence. Add producer-specific story adapters for RomeBots, SpaceDrivel and polbots when their own state/report hooks exist. Upcoming programs and matches are announcements, not completed results.

### Reddit and outside reporting

Reuse Reddit thread acquisition for topics and quoted reactions, but preserve comment IDs/permalinks where needed. Comments can be staged as an attributed interview montage or bystander-style presentation; do not claim a commenter witnessed the event unless the source says so. Keep sourced quotes distinct from generated actors interpreting them. Actual articles/feeds should carry more context than headlines alone. Source selection, cost and retrieval method remain research decisions.

### Weather and internet weather

Two related segment concepts are available: literal weather for an explicitly named place, and “internet weather” about what people are dooming about, why the explanation may be wrong, and what the sources actually support. The latter is a presenter personality/segment, not a meteorological API feed. It can use the same graphic set and review packet without pretending unsupported claims have been conclusively debunked.

For a US forecast slice, the [official NWS API documentation](https://www.weather.gov/documentation/services-web-api) covers free forecast/alert data, required identifying User-Agent headers, point-to-grid discovery and rate limits. Resolve `/points/{latitude},{longitude}` and follow its forecast URL; active alerts can be filtered by state. Preserve issue/valid times and named location. Forecast discussions need a separate endpoint/product check before choosing an adapter. Map imagery is separate work: NWS radar-status endpoints do not supply radar display images.

Start with a saved forecast fixture and a static image. Store markers as image-normalized coordinates with labels; separately author a mapping from those markers to stage-space pointing targets. Validate image aspect ratio and camera framing before adding hand IK. Do not make the language model guess hand coordinates or generate a map that appears to be an official forecast.

## Anchors and performance

The inventory now has 60 actor assets with descriptive identities, pronouns and provisional Gemini voices. Develop a fixed anchor ensemble and a separate fixed production crew. Other formats select presenters/guests for their subject. Paul can be a political analyst rather than the sole anchor. Use existing actors as preview stand-ins first. Future persisted casting must preserve replay identity, but its storage contract remains undecided. Keep the working fixture host in production until matching profiles and format prompts are authored. Rotation is optional later work, not the next prerequisite. Missing cast assets need a known fallback host.

User work: choose ensemble membership, personalities, relationships and room-specific blocking; audition and refine existing voice assignments. Runtime work: validate cast/spawn capacity, apply format and actor prompt profiles, and persist assignments. Do not require two formats in a single bulletin to validate casting or blocking. Progressive rigging/IK polish should improve an already watchable performance; begin with existing idle/talk animations and authored poses.

## Current studio work and handoff

Follow the [studio roadmap](PBNNStudioRoadmap.md) for priorities and the [Unity handoff](PBNNUnityHandoff.md) for the executable verification workflow.

| Work | Supported workflow | Completion evidence / remaining check |
| --- | --- | --- |
| Desk regression baseline | Keep Business Man 1, saved Desk placement, Wide shot and existing `pbnn.media.v1` fixture | Refine angle, host placement, TV and lower third; confirm generation, save/reload, replay, skip and channel return in Unity |
| Five-location blocking | Inspect Desk, Lounge, Office, Corner and Video Wall independently with Studio Preview and existing actor stand-ins | Check registered spawns, anchor/target/target-group references, eligible camera and readable framing for each room; author missing seats/positions |
| Local displays | Test TV framing, board readable area and useful curved-wall regions with local images | Only the Desk TV and local overlay have runtime fixture support; other surfaces need authored display handling and separate verification |
| Casting exploration | Use temporary preview cast and poses; develop fixed anchors, production crew and subject-specific presenters/guests | Final ensembles, relationships, actor profiles and format prompts remain user/creative work; do not add all 60 actors to generation |
| Content/playback boundaries | Record actual room interaction needs after layout/display checks | Chat versus scene versus segment, transitions and any persistence contract remain undecided |

The old P3 fixed-shot director/cubicle cutaways, saved node cues and deterministic two-segment/two-set switch are superseded next-step proposals. P0/P1 channel shell and the bounded P2/P4 media slice already exist; do not rebuild them or restore the removed Studio Rundown generator. Live ingestion, votes, scheduling, staffing/rotation, transition orchestration, video, advanced walking and IK remain later work. The studio floor has no registered program location and stays postponed.

Preserve concurrent soccer/UI work. Keep context key `pbnn` distinct from scene object name `PBNN`. Report changed files, validation and exact remaining Unity steps. Static/serialization checks and Scene-view previews do not prove Game-view camera selection, TTS, replay or cleanup. No production generation change is required for this documentation alignment.
