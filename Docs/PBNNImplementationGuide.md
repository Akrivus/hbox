# PBNN implementation guide

PBNN (Polbots News Network) replaces AppyDays as HBOx’s informational broadcast. Start with a saved fixture bulletin, then add independent ingestion pipelines and broadcast presentation. This is an implementation inventory and handoff, not a commitment to build every brainstorm. See [Channel backlog](ChannelBacklog.md) for the wider direction and completed soccer work.

P0/P1 implementation status: the PBNN scene/context, channel/build registration, isolated placeholder Walt actor, fixture prompts/input and editor fixture action are now present. Local channel config enables PBNN. Live Unity generation/save/replay verification is pending; see [PBNN Unity handoff](PBNNUnityHandoff.md) for asset inventory, validation and exact editor steps. P2/P3 are not implemented yet.

## First playable milestone

One anchor presents two short segments from preserved source packets. The scene uses two existing or placeholder sets with fixed camera views. Each segment has a title, source references, one optional image, and explicit playback cues. Save and replay the same bulletin; switch away and back without stranded cameras, images, or sound. Live ingestion, anchor rotation, weather maps, IK, and field reports do not block this milestone.

Daily bulletins may run roughly 3–15 minutes when the material supports it. No padding requirement. Paul Bott is one possible anchor, not the only host. Models/assets are available, but casting and personalities need user development. Here “model” means a character model; do not assume each anchor requires a different LLM.

## Existing code and limits

| Area | Existing entry points | Reuse and remaining work |
| --- | --- | --- |
| Channel registration | `Assets/Main.unity`, `ChatManagerContext`, `RemoteControl`, `ProjectSettings/EditorBuildSettings.asset` | New pbnn context, matching scene/name, Main entry, enabled config, build registration, prompts, actors and spawn points. Preserve AppyDays as reference until migration works. |
| Humanoid assets | `Assets/Scenes/AppyDays.unity`, `Assets/Scenes/AppyDays/Actors`, `Assets/Scenes/AppyDays/Prefabs/Actors` | PBNN scene copies the office staging and reuses Walt's visual through a separate actor definition. Inventory and live framing checks are recorded in the Unity handoff. |
| Generation/save | `Assets/Core/ChatGenerator.cs`, `Assets/Core/DataModels/Chat.cs` | Direct Idea queue already bypasses Reddit acquisition. GeneratedData can carry a namespaced serialized packet after round-trip validation. Normal generation saves and queues playback; inspect the current Generate routine before adding background production. |
| Locations | `Assets/Core/Integrations/Location/LocationManager.cs`, `LocationDefinition.cs` | Chat.Location loads a location prefab on queue-take and assigns actor spawn points. This is episode-level staging, not an existing segment-by-segment director. |
| Cameras | `Assets/Core/SpawnPointManager.cs` | Virtual-camera array exists; node activation resets its cameras' priorities and samples a camera. PBNN uses predefined fixed virtual shots; configure a single eligible shot per active segment or a separately controlled higher-priority broadcast camera. Verify Cinemachine selection and framing with the actual scene wiring. |
| Playback cues | ChatNode.Actions, ChatManagerContext.OnChatNodeActivated | Existing action extraction and node activation are candidate cue boundaries. Add PBNN-scoped handling; inspect when activation fires relative to audio and skip before choosing cue timing. |
| Pitch review | `Assets/Core/Integrations/Sources/RedditPitchCandidateService.cs`, `RedditPitchGate.cs`, `RedditSource.cs` | GenerateAsync evaluates the pitch and returns null on rejection. Source then auto-queues accepted candidates or posts for voting. Reuse the flow; its source model, prompts, store names and converter are Reddit-specific. |
| Discord | `Assets/Core/Integrations/DiscordBotService.cs`, `DiscordParticipationPolicy.cs` | Gateway handles reactions/interactions and vote integration. Dispatch currently has no MESSAGE_CREATE intake. Tips text/attachment acquisition is new work. |
| Vote/config controls | `Assets/Core/Integrations/Config/RedditConfigs.cs` | Existing batch, auto-approval count, minimum votes, score, expiry, and active-window settings are references. Enlarging batch size alone does not define bulletin segment voting. |
| HBOx reports | SoccerEventStore, ServerSource `/api/soccer/reports?limit=50` | Soccer emits real match results. Use as first report adapter; RomeBots and polbots story-report hooks remain future work. |
| Shared UI policy | `Assets/Core/UI/UiVisibilityCoordinator.cs`, `UiVisibilityPolicy.cs` | Keep global visibility rules here. PBNN’s ticker, image panel and segment transitions belong to its local presentation controller; avoid scattered global show/hide rules. |

LocationManager needs a focused lifecycle check before reuse: it assumes a nonempty Locations array and destroys the previous LocationDefinition component rather than explicitly removing the instantiated root. Confirm prefab ownership and cleanup; do not stack old sets by accident. Context event wrappers forward shared ChatManager events, so PBNN listeners must check current channel/episode ownership and unsubscribe on teardown.

## Proposed data flow

Tips, articles, Reddit material, weather, and HBOx reports feed independent adapters. Each adapter preserves originals and produces a common source packet. Review accepts or rejects a candidate. Accepted candidates either enter the bulletin pool immediately or wait for a vote. A compiler selects stories and produces a rundown; generation writes performance text against that rundown. Playback consumes saved segment cues and media references.

Auto-approval means skipping the audience vote after review accepts a candidate. It does not bypass review and is not proof a factual claim is true. Keep rejected candidates out of the production pool. Voting chooses inclusion/priority, not factual accuracy. Trusted in-universe match results can use a simpler editorial suitability check than unverified submitted claims.

### Minimal saved records

| Record | Required information |
| --- | --- |
| Source packet | ID, kind, origin/author, source URL/message/report ID, publication time if known, retrieval/submission time, location if relevant, preserved text, attachment references, factual notes and attributed claims |
| Candidate | ID, source IDs, segment proposal, review result/reason, status, vote binding and expiry when applicable |
| Rundown | Bulletin ID/date/coverage window, selected anchor IDs, ordered segments, source IDs and media references |
| Segment | ID/type, set ID, fixed camera ID, speakers, script/node boundary, headline/lower third, image/map reference, caption, cue list |
| Media asset | ID, stored relative path, type, origin/capture time, attribution, caption, optional source ID and map markers |

These are proposed contracts, not existing types. Start with a versioned `pbnn.rundown` payload in GeneratedData if that fits the actual save pipeline. Keep originals separate from generated performance text. Verify media paths after save/reload; downloading an attachment without persisting its link to the story is insufficient. Replays must not ingest or consume the same items again.

Awaiting saves is not a prerequisite. For live production, a save-success event can commit a recoverable reservation after the file write completes. The current `Chat.Save()` is `async void` and exposes no success notification, so generation completion alone must not commit that reservation. Alternatively, poll known producer report routes, durably record reports by stable ID, and pick up new reports on the next collection pass. Track collected report IDs separately from inclusion in a saved bulletin: acquisition deduplication does not mean a story has aired or been consumed. Implement bulletin inclusion tracking only when live compilation needs it; saved fixtures do not require a reservation system.

## Sets and fixed cameras

A cross-shaped studio is optional. Isolated cubicles or disconnected prefab sets can provide the same views; physical adjacency is not a broadcast requirement. Use a fixed number of predefined shots at first. Choose one rendered camera with fixed virtual shots where existing Cinemachine wiring fits, or a controlled camera switch if required; assign one owner and keep one effective audio listener.

| Segment | Initial staging | Later options |
| --- | --- | --- |
| Headlines | Anchor/desk, headline UI, corner image | More anchors, full-screen image or b-roll |
| Weather / internet weather | Presenter beside image panel | Map markers, pointing targets, IK |
| Interview | Two chairs and fixed two-shot | Interview cutaways and remote testimony |
| Desk conversation | Two anchors at a desk | Reactions and handoffs |
| Special / field report | Separate location prefab and fixed shot | Scenic backdrop, foreground props, passing cars and environmental sound |

Use **one Chat per bulletin**. Segments are saved boundaries within that Chat, with explicit set, anchor and fixed-shot references. Current location loading remains episode-level; load or retain a studio containing the required cubicles rather than asking LocationManager to change location for every segment.

Cubicle-style staging provides anchor cutaways: anchors occupy stable positions in isolated sets, and predefined fixed virtual cameras make the cutaway automatically at a segment boundary or an explicitly saved speaker cue. Physical movement between cubicles is unnecessary. Start with one host and shot, then two permanently available sets and shots. Additional anchors can use the same routing once their assets are assigned. Dynamically loaded specialty locations come after lifecycle behavior is proven.

Prefer fixed virtual-camera configuration over suspending the existing speaker behavior. A single eligible camera in the active SpawnPointManager makes its per-node selection deterministic; a broadcast camera outside its managed array can instead retain a higher priority. Choose the smallest approach that works with the scene's active managers and Cinemachine brain. A fixed transform alone does not prevent another virtual camera from winning priority, and existing target-follow components must not move the authored shot. Verify both selection and framing across consecutive speakers and cutaways.

### Presentation states and cues

Proposed local states: opening, transition, segment, closing, idle. A segment selects its set/camera, clears prior media, applies headline/lower-third state, and shows its image when cued. Transitions can play a swoop/sting and a generic station voice; persist the chosen cue so replay stays consistent. Ticker text comes from saved approved headlines/announcements, with the bulletin date visible on older replays.

Use stable segment IDs and node-boundary cues initially, rather than invented frame-accurate timestamps. Clear UI/audio and restore camera ownership on skip, replay start, channel change, or interrupted transition. Unknown set/camera/image IDs use a defined default or omit the image; they must not leave the previous story’s picture showing. Keep full-screen video and streaming footage for later.

`OnChatNodeActivated` fires before `ActorController.Activate`, making it suitable for immediate camera and graphic cues. It is a synchronous event, so it cannot by itself hold speech until a transition sting finishes. Initially use immediate cuts; add an explicit coroutine playback boundary only when a transition needs to delay speech.

## Independent ingestion pipelines

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

Create a roster of available actor assets with stable IDs, voice settings, role, personality notes, and eligible segment types. Paul can share the schedule with other anchors. Select an anchor or pair when composing the rundown and save that choice; replay should not recast the episode according to today’s rota. Start with an explicit fixture host, then two alternating scheduled slots. Missing cast assets need a known fallback host.

User work: choose personalities, models, voice identities and combinations. Runtime work: validate roster IDs, select scheduled hosts, apply prompt profiles, and persist assignment. Avoid elaborate staffing rules until a two-host rotation works. Progressive rigging/IK polish should improve an already watchable performance; begin with existing idle/talk animations and authored poses.

## Afternoon-sized implementation inventory

| Task | Build autonomously | Unity or creative handoff | Completion check |
| --- | --- | --- | --- |
| P0 Inventory and fixture | Record reusable wiring, create a small source/rundown fixture | Pick two usable sets and one host | Concrete asset list and runnable fixture plan |
| P1 Channel shell (N1) | Context/config/prompts/registration changes where serialized edits are reliable | Confirm prefab refs, spawns, camera and build scene | One short segment generates, plays and replays as pbnn |
| P2 Saved packet (N2) | Versioned source/rundown payload and media references | Supply one local image | Save/reload preserves sources, host, set and image |
| P3 Fixed shot director | Set/camera lookup, node cues and lifecycle cleanup | Assign two shots, actor placements and transition duration | Two fixture segments switch shots deterministically |
| P4 Image and lower third (N6) | Headline, caption, image on/off cue handling | Position UI and choose fonts/layout | Correct image appears; skip clears it |
| P5 Ticker and audio (N6) | Saved ticker items, sting/station-voice cue routing | Choose clips, levels and motion | Speech readable; no stale ticker or overlapping sting |
| P6 Soccer adapter (N2b) | Report discovery/dedup and packet conversion | Inspect a saved bulletin | Real 0–0 result survives into sourced segment |
| P7 Tips text (N4a) | Scoped intake, durable dedup and neutral-source review adapter | Configure one Discord channel and permitted participants | Rejection blocks; accepted tip elevates once |
| P8 Tips attachments | Store images, extraction boundary and packet linkage | Submit example screenshots | Original and extracted text reload correctly |
| P9 Optional story votes | Candidate-to-segment selection, expiry and no-vote policy | Choose voting/batch settings | Rejected tips never reach vote; winning item enters one rundown |
| P10 Anchor rotation | Persisted host rota assignment | Develop two personalities/voices | New slots change host; old bulletin keeps cast |
| P11 Daily production (N5) | Recurrence and save-only disposition if needed | Choose coverage window/time | Background bulletin saves without channel switch |
| P12 Weather fixture | Forecast packet adapter and static graphic cues | Choose location or internet-weather framing | Location/time/source shown; no invented weather |
| P13 Interviews/field set | Explicit segment and location routing | Build one two-chair or scenic field-report set | Attributed material plays with intended shot |

P2 precedes live adapters. P3/P4 can progress with fixtures while ingestion is unfinished. Implement P5 audio and ticker as separate changes if necessary. TV delivery/program blocks remain N8/N9 in the backlog. B-roll/video, automatic screenshot capture, map animation and hand IK are later slices. P0/P1 channel wiring and local configuration are implemented as described in the Unity handoff; live Discord intake is unchanged.

## Handoff to the implementation thread

### Work implementable now

The following work can proceed without final anchor personalities, a finished studio layout, live Discord intake, or weather geography. This is an implementation readiness list; none of these changes is implemented by this document.

| Slice | Implement now | Remaining Unity/user work | Validation |
| --- | --- | --- | --- |
| P0 inventory | Inventory AppyDays actors/prefabs, scene component references, prompt/config resolution and camera wiring; author a one-segment briefing fixture and two-segment rundown fixture | Confirm placeholder host and usable cubicle assets | Resolve each proposed reference to an existing asset or an explicitly named placeholder |
| P1 channel shell | Add pbnn identity, minimal broadcast prompts, fixture input and reliable scene/Main/build registration edits after inventory | Confirm serialized references, host spawn, rendered camera and enabled-channel config | Generate, play, save and replay one short bulletin in Unity |
| P2 saved rundown | Add versioned source/rundown contracts, serialization/validation helpers and fixture round-trip checks using `Chat.GeneratedData` | Supply a local image when image resolution is tested | Sources, bulletin date, host, segment boundaries and shot IDs survive reload; reject unsupported payload versions clearly |
| P3 fixed cutaways | Add PBNN-local set/shot lookup and saved node-boundary routing; start with immediate cuts and explicit fallback IDs | Assign two cubicles, fixed cameras and anchor positions; verify target-follow settings | Same saved bulletin makes the same cuts across replay and speaker changes; skip/channel changes clear local state |
| P4 graphics | Add local headline/caption/image cue handling and media-path resolution | Place the panel and choose fonts/framing | Reload resolves the image; missing images and skip clear the previous image |
| P6 report acquisition | Add soccer report-to-source conversion and a durable MatchId-based collection index against the existing report route | Inspect the sourced sports segment after P1/P2 | Collect the same report twice with one stored source packet and preserve the actual score |

Recommended next implementation scope: P0/P1 to establish the playable channel, followed by P2/P3 for one saved bulletin with two fixed cubicle shots. P2 contracts and the P6 conversion can be built with fixtures while scene wiring is being confirmed. Keep P4 as a separate visible slice. Save notifications/reservations become necessary when automatic production commits inclusion; they do not block these fixture milestones. Discord gateway changes, recurring production, votes, anchor rotation, weather ingestion and timed transition audio remain later work.

Start with P0/P1, not all ingestion pipelines. Audit the actual workspace and preserve concurrent soccer/UI changes. Use `pbnn` as the proposed context key, checking collisions and exact scene-name conventions before registering it. Reuse AppyDays humanoid assets; leave the archived scene available for reference. Read this guide and ChannelBacklog.md, then produce one playable fixture segment and explicit Unity wiring instructions. Continue code/config work until only asset selection, placement or live editor confirmation requires the user. Do not rewrite soccer or introduce a universal mode framework.

Report each completed slice with changed files, validation, and the exact remaining manual steps. Once the single segment works, build saved packets and a deterministic two-set switch. Ask for personalities or final layout only when that choice blocks the current slice. The fixed number of cameras, extra anchors and segment catalog are design directions; the plus-shaped floor plan, exact rota, tips submission policy and weather geography remain open choices.
