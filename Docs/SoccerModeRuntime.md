# Soccer Mode Runtime

This document describes how soccer mode currently works in HBOx after the embedded-match integration, interrupt queueing, and packet-bank refactors.

It is meant to be the cleanup baseline before further refactors and before moving more hard-coded strings into Vault prompts.

## Goals

- Keep soccer inside the normal HBOx content pipeline.
- Use the Football Simulator as an embedded subsystem, not a separate runtime universe.
- Let full-scene soccer content still flow through `Idea -> ChatGenerator -> ChatManager`.
- Let live soccer reactions land as short injected micro-chats instead of spawning full chats on every event.

## High-Level Runtime

There are now two soccer content paths:

1. Full-scene path
   - Pregame, postgame, hearings, rivalry fallout, and similar larger scenes become normal `Idea`s.
   - Those ideas flow through the existing chat generation pipeline.

2. Live interrupt path
   - Match events become `SoccerEventSummary` objects.
   - The interrupt service resolves or consumes a short packet.
   - The packet is converted to `ChatNode`s and injected into the currently playing scene.

## Main Runtime Objects

### [`SoccerGameSource.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerGameSource.cs)

The orchestration entry point.

Responsibilities:

- Registers soccer config and match event listeners.
- Decides when to launch a match from the current scene.
- Loads the Football Simulator startup scene additively.
- Disables vendor startup behaviors that would otherwise overwrite the host scene.
- Creates and starts matches through `MatchEngineLoader`.
- Owns shutdown and emergency teardown.
- Owns broadcast/camera binding and watchdog behavior.
- Routes full-scene generation to `SoccerIdeaService`.
- Routes live event summaries to `SoccerInterruptService`.
- Ticks deferred interrupt injection each frame.

`SoccerGameSource` should stay an orchestrator, not become the place where prompt text, packet policy, or narrative state grows indefinitely.

### [`SoccerMatchStateService.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerMatchStateService.cs)

The authoritative live soccer state holder.

Responsibilities:

- current match id
- current phase (`Pregame`, `Live`, `Postgame`, etc.)
- recent residue queue
- event sequence numbering
- score-sensitive world fingerprint generation
- converting raw football events into `SoccerEventSummary`

This is the current source of truth for whether a live packet is stale.

### [`SoccerInterruptService.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerInterruptService.cs)

The live interrupt subsystem.

Responsibilities:

- prewarming packet banks before kickoff
- maintaining pre-generated packet queues
- building packets through prompt resolution + LLM + sentiment + TTS
- assigning packets to banks
- deciding whether a packet can inject now
- holding one deferred pending packet when another interrupt is already live
- dropping stale or superseded packets

This is the core of the “soccer interrupts should feel invisible” behavior.

### [`SoccerInterruptPolicy.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerInterruptPolicy.cs)

The small routing/policy helper.

Responsibilities:

- maps summaries to packet banks
- defines target counts for banks
- defines supersession rules for pending packets

This is intentionally small and should remain the place for “who wins when two soccer beats compete.”

### [`SoccerIdeaService.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerIdeaService.cs)

Thin adapter for full-scene content.

Responsibilities:

- queue one pregame `Idea`
- queue postgame fallout `Idea`s

This keeps full-scene soccer content on the normal pipeline instead of creating a parallel chat stack.

### [`SoccerIdeaComposer.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerIdeaComposer.cs)

Current full-scene text composer.

Responsibilities:

- builds the current pregame idea text
- builds postgame idea text
- optionally builds integrity-hearing and rivalry-fallout ideas
- uses `Highlight Prompt` from Vault when available

This file currently still contains several hard-coded strings that are good candidates for migration into Vault.

## Match Lifecycle

The current embedded match flow is:

1. `SoccerGameSource` decides to start a match from scene context.
2. Teams are selected and renamed to match current actors.
3. `SoccerMatchStateService.BeginMatch(...)` sets pregame state.
4. Football startup scene loads additively.
5. Vendor `Boot` and `DefaultSceneLoader` are disabled.
6. `SoccerInterruptService.BeginMatch(...)` starts packet-bank prewarming.
7. `SoccerIdeaService.QueuePregameIdea(...)` queues the pregame full-scene idea.
8. Optional prewarm wait runs before kickoff.
9. One short pregame packet may inject before kickoff.
10. `MatchEngineLoader.CreateMatch(...)` and `StartMatchEngine(...)` start the simulator.
11. Match phase flips to `Live`.
12. Football camera is claimed and bound to the media-screen render texture.
13. Live football events feed the interrupt and residue systems.
14. Final whistle ends live interrupts and queues postgame ideas.
15. Match unload tears down football scenes and tracked `DontDestroyOnLoad` objects.

## Live Interrupt Path

The live path today is:

1. Football Simulator emits an event.
2. `SoccerGameSource.HandleInjectableEvent(...)` turns it into a log line.
3. The log is appended to `gameEventLog` and recent residue.
4. `SoccerMatchStateService.BuildSummary(...)` produces a `SoccerEventSummary`.
5. `SoccerInterruptService.TryInject(...)` looks for a ready packet in the matching bank.
6. If needed, a packet is generated on demand.
7. Stale or superseded packets are dropped.
8. If another interrupt is already active, one pending packet can wait behind it.
9. When the path is clear, the packet injects via [`ChatManager.InjectNodes(...)`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Core/ChatManager.cs).

Injected interrupt nodes are marked internally so the service can detect “an interrupt is still pending/playing” without guessing from event timing.

## Packet Banks

Packet banks now exist conceptually and in code:

- `Pregame`
- `LiveGeneric`
- `LiveScoreSensitive`
- `Broadcast`

Current usage:

- `Pregame`
  - short pre-kickoff beats
- `LiveGeneric`
  - whistles, tackles, near misses, saves, general procedural noise
- `LiveScoreSensitive`
  - goals and other beats that should supersede older weaker packets
- `Broadcast`
  - reserved for feed-loss / screen-share incidents, not fully used yet

## Ordering And Supersession

The current anti-backwards-reaction behavior depends on:

- monotonic `Sequence` values from `SoccerMatchStateService`
- score-sensitive `WorldFingerprint` values
- one deferred pending packet slot
- supersession rules in `SoccerInterruptPolicy`

Practical effect:

- older score-sensitive packets can be dropped if a newer score event overtakes them
- a live packet won’t inject while another soccer interrupt is still pending in playback
- a newer packet can replace the pending packet if it is more important

This is why the soccer interruptions now read as part of the scene instead of as obvious async arrivals.

## Full-Scene Content Path

Full-scene soccer content still goes through the normal HBOx path:

1. soccer service creates an `Idea`
2. `ChatGenerator` resolves prompts
3. generator components enrich chat and nodes
4. `ChatManager` queues and plays the resulting chat

Current full-scene soccer triggers:

- pregame
- postgame fallout
- integrity-hearing fallout
- rivalry fallout

These are still text-composed in code today by [`SoccerIdeaComposer.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerIdeaComposer.cs).

## Prompt Surface Today

Soccer mode already uses Vault for part of its generation surface:

- [`Vault/polbots/Prompts/Soccer Mode/Dialogue Generation.md`](C:/Users/akriv/OneDrive/Desktop/HBOx/Vault/polbots/Prompts/Soccer%20Mode/Dialogue%20Generation.md)
- [`Vault/polbots/Prompts/Soccer Mode/Sentiment Tagger.md`](C:/Users/akriv/OneDrive/Desktop/HBOx/Vault/polbots/Prompts/Soccer%20Mode/Sentiment%20Tagger.md)
- [`Vault/polbots/Prompts/Soccer Mode/Behavior Generation.md`](C:/Users/akriv/OneDrive/Desktop/HBOx/Vault/polbots/Prompts/Soccer%20Mode/Behavior%20Generation.md)
- [`Vault/polbots/Prompts/Soccer Mode/Highlight Prompt.md`](C:/Users/akriv/OneDrive/Desktop/HBOx/Vault/polbots/Prompts/Soccer%20Mode/Highlight%20Prompt.md)

Current hard-coded strings still living in code include:

- seed packet log text in [`SoccerInterruptService.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerInterruptService.cs)
- pregame idea framing in [`SoccerIdeaComposer.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerIdeaComposer.cs)
- postgame / hearing / rivalry idea framing in [`SoccerIdeaComposer.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerIdeaComposer.cs)
- actor-affinity labels in [`SoccerInterruptService.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerInterruptService.cs)
- context block formatting in [`SoccerInterruptService.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerInterruptService.cs)

These are the main cleanup candidates for prompt migration.

## Config Surface Today

Current soccer config in [`SoccerConfigs.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/Config/SoccerConfigs.cs) controls:

- event log line templates
- match time limit
- time between games
- crowd/audio volume behavior
- whether scenes clear on game end
- whether games auto-start on scene start / batch end / match end

Important detail:

- event log lines are already configurable in `SoccerConfigs`
- interrupt seed logs are not yet configurable the same way
- full-scene idea framing is not yet configurable the same way

## Broadcast / Share-Screen Path

The match is currently broadcast into polbots through:

- football camera ownership in [`SoccerGameSource.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/Integrations/SoccerGameSource.cs)
- UI layout changes in [`ShareScreenUIManager.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/UI/ShareScreenUIManager.cs)
- call-screen enable/disable in [`VideoCallUIManager.cs`](C:/Users/akriv/OneDrive/Desktop/HBOx/Assets/Scenes/polbots/Scripts/UI/VideoCallUIManager.cs)

Current behavior:

- match start enables share-screen mode
- a watchdog rebinds the render texture if the football camera drops it
- signal lost/restored events exist and can later become commentary triggers

## Vendor Dependencies

Soccer mode currently depends on the local Football Simulator patches documented in:

- [`FootballSimulatorVendorPatches.md`](C:/Users/akriv/OneDrive/Desktop/HBOx/Docs/FootballSimulatorVendorPatches.md)

Those patches are part of the current runtime contract. Cleanup work should not silently assume stock vendor behavior.

## Cleanup Priorities

Recommended cleanup order:

1. Move hard-coded interrupt seed strings into Vault prompts.
2. Move pregame/postgame/hearing/rivalry idea framing into Vault prompts.
3. Keep runtime soccer state injection in code.
4. Keep packet policy in code.
5. Keep Football Simulator lifecycle safeguards in code.

That split keeps prompts editable while leaving fragile runtime sequencing in C#.

## Proposed Prompt Migration Targets

Best first prompt extractions:

### 1. Interrupt seed prompt templates

Move these out of `BuildSeedLog(...)`:

- pregame interrupt beat
- goal beat
- whistle/procedure beat
- save beat
- near-miss beat
- tackle beat

Suggested destination:

- `Vault/polbots/Prompts/Soccer Mode/Interrupt Seeds.md`

### 2. Full-scene idea prompt templates

Move these out of `SoccerIdeaComposer`:

- pregame idea framing
- postgame fallout framing
- integrity-hearing framing
- rivalry-fallout framing

Suggested destination:

- `Vault/polbots/Prompts/Soccer Mode/Idea Seeds.md`

### 3. Actor-affinity guidance

Move these out of `BuildAffinity(...)` and `BuildContext(...)` if we want easier tuning of:

- UN legitimacy language
- Security Council / referee-adjacent framing
- home/away stakeholder framing

Suggested destination:

- `Vault/polbots/Prompts/Soccer Mode/Interrupt Context.md`

## Keep In Code

These pieces should remain code-driven:

- scene lifecycle and teardown
- camera ownership and render-texture watchdog
- packet-bank structure
- sequence / fingerprint stale detection
- pending-packet supersession rules
- event subscriptions to Football Simulator

These are runtime mechanics, not prompt content.

## Current Cleanup Question

The next cleanup pass should not be “move everything into prompts.”

It should be:

- keep runtime policy in code
- move tone/framing strings into Vault
- keep runtime state injection structured and explicit
- make soccer mode easier to retune without reopening C#

That preserves the working system while making the expressive layer configurable.
