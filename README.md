# HBOx

HBOx is a Unity project for generating and replaying AI-driven "Chats": short staged scenes with a cast, location, dialogue, sentiment, timing, and audio. The project started as `polbots`, but it now acts more like a shared runtime for multiple shows and worlds.

The current project is split into two main layers:

- `Assets/Core`
  Shared engine code for chat generation, playback, actor control, UI, config loading, external integrations, replay loading, and prompt resolution.
- `Assets/Scenes`
  Show-specific scenes, actors, prefabs, audio, and scene adapters.

## How it works

At a high level, the pipeline looks like this:

1. A source provides an `Idea`.
   Sources include replay folders, Reddit batches, HTTP endpoints, and scene-specific systems like the `polbots` soccer integration.
2. `ChatGenerator` turns the idea into a `Chat`.
   It resolves prompt files from `Vault`, asks the LLM for topic/cast/location details, then runs attached generator components to fill in dialogue, reactions, voice lines, vibe, memories, and related metadata.
3. `ChatManager` queues and plays the chat.
   It switches to the correct scene/context, loads staging information, spawns actors, runs intermission hooks, and activates each `ChatNode` in sequence.
4. `ActorController` and scene components perform the scene.
   Shared actor subsystems handle animation, face/lips, camera look targets, voice playback, items, subtitles, and scene-specific behavior.

## Scene responsibilities

### `Assets/Scenes/polbots`

The original project, and still the heaviest scene-specific code layer.

Responsibilities:

- video-call presentation and per-actor cameras
- globe-mode staging and map-driven positioning
- country/flag-driven visuals
- soccer match integration that can emit live ideas back into the chat pipeline

### `Assets/Scenes/RomeBots`

Mostly a content/world pack on top of the shared core. It currently has very little custom C# and relies primarily on shared generation/playback behavior plus its own actors, prefabs, and assets.

### `Assets/Scenes/AppyDays`

Uses the shared core runtime with show-specific scene assets and actor data. No custom scene-side C# currently lives here.

### `Assets/Scenes/SpaceDrivel`

Also uses the shared core runtime with its own cast, prefabs, and assets, with no current scene-side C# layer.

## Important folders

- `Assets/Core`
  Runtime systems shared across all shows.
- `Assets/Scenes`
  Show scenes and content.
- `Vault`
  Prompt, input, output, and other text assets used during generation.
- `MemoryCaptures`
  Memory-related data produced by the project.
- `replays-*.txt`
  Local replay history trackers.
- `reddit-*.txt`
  Local Reddit history trackers.

## Configuration

The project loads `config.json` from the executable root path through `ConfigManager`.

Each config entry must contain a `Type` field. Only the systems present in your scene/context need to be configured.

Example:

```json
[
  {
    "Type": "openai",
    "ApiUri": "https://api.openai.com",
    "ApiKey": "YOUR_OPENAI_API_KEY",
    "SlowModel": "gpt-4o",
    "FastModel": "gpt-4o-mini",
    "UseEmbeddings": false
  },
  {
    "Type": "tts",
    "GoogleApiKey": "YOUR_GOOGLE_TTS_KEY",
    "OpenAiApiKey": "YOUR_OPENAI_TTS_KEY"
  },
  {
    "Type": "discord",
    "AvatarURL": "https://...",
    "WebhookURLs": {
      "#stream": "YOUR_STREAM_WEBHOOK_URL"
    },
    "EnableBot": true,
    "BotToken": "YOUR_BOT_TOKEN",
    "ApplicationId": "YOUR_APP_ID",
    "SlashCommandGuildIds": ["YOUR_TEST_GUILD_ID"],
    "EnableIdeaCommand": true,
    "DefaultDailyIdeaLimit": 3,
    "BoosterDailyIdeaLimit": 10,
    "BoosterRoleIds": ["BOOSTER_ROLE_ID"]
  },
  {
    "Type": "folder",
    "ReplayDirectory": "polbots",
    "ReplayRate": 80,
    "ReplaysPerBatch": 20,
    "MaxReplayAgeInMinutes": 1440
  },
  {
    "Type": "reddit",
    "SubReddits": {
      "worldnews+anime_titties+todayilearned": "Default",
      "AskHistorians+UnitedNations+geopolitics": "History",
      "Africa+China+america+australia+europe": "Regional"
    },
    "MaxPostAgeInHours": 24,
    "BatchSize": 20,
    "BatchSizeLimit": 20,
    "BatchIterations": 1,
    "BatchPeriodOffset": "00:00",
    "BatchPeriodInMinutes": 480,
    "ActiveHoursStart": "00:00",
    "ActiveHoursEnd": "23:59",
    "EnablePitchGate": true,
    "PitchDiscordChannel": "#stream",
    "PitchExpirationMinutes": 180,
    "PitchAutoApprovalBatchSize": 0,
    "PitchMinimumVotesToQueue": 1,
    "MaxDepth": 3,
    "TopRoots": 3,
    "TopLevelLimit": 30,
    "PerLevelChildLimit": 20,
    "MaxDialogueLines": 16,
    "MaxCharsPerLine": 280,
    "Sort": "confidence"
  },
  {
    "Type": "obs",
    "VideosFolder": "C:/Videos/HBOx",
    "OBSWebSocketURI": "ws://localhost:4455",
    "IsStreaming": false,
    "IsRecording": false,
    "DoSplitRecording": true,
    "OnlyNewEpisodes": true
  },
  {
    "Type": "splash",
    "Splashes": [
      "Tonight on HBOx",
      "Previously on nothing"
    ],
    "TitleDuration": 5.0,
    "SplashDuration": 2.0
  }
]
```

### Notes

- Remove a config block entirely to disable that integration.
- `folder` is the active replay loader type registered at runtime.
- `reddit.SubReddits` is a dictionary, not a simple string array.
- `reddit.EnablePitchGate` changes Reddit intake from direct `RedditSource -> Idea` generation into `RedditSource -> PitchCandidate -> Discord vote -> Idea`.
- `reddit.PitchDiscordChannel` must match a configured Discord webhook key, usually `#stream`.
- Pitch votes resolve after the voting window closes: more thumbs-up than thumbs-down queues the pitch as an `Idea`; more thumbs-down rejects it; ties or too few votes expire.
- `reddit.PitchExpirationMinutes` is how long a posted pitch can receive approval votes before it is treated as stale.
- `reddit.PitchAutoApprovalBatchSize` is how many top evaluator-approved Reddit pitches can skip Discord voting and queue directly per active Reddit batch slot. Set it to `0` to require voting for every pitch.
- `reddit.PitchMinimumVotesToQueue` is the minimum total vote count required before an expired positive voted pitch can queue.
- Autoapproved pitch candidates are sorted by Reddit comment count weighted most heavily, then Reddit karma, then newest post first.
- Pitch generation uses `Vault/{show}/Prompts/Reddit Source/Pitch Candidate.md`, then `Vault/{show}/Prompts/Reddit Source/Pitch Evaluator.md` to reject weak or overlong pitches before posting.
- Posted pitch cards surface the pitch, cast, source subreddit, Reddit karma/comment counts, and evaluator approval reason; the approved `Idea` still includes the raw Reddit post text and mined thread material for generation context.
- In pitch-gate mode, Reddit posts are only written to the local seen history after a pitch is accepted for voting, so evaluator-rejected posts can be reconsidered later.
- Approved pitch posts are pinned by the Discord bot when bot permissions allow it, and the operator panel reads `/api/pitches` for the current pitch deck.
- OpenAI text generation and OpenAI TTS are configured separately.
- Some integrations only matter in specific scenes, such as `soccer` in `polbots`.

### Soccer config example

`soccer` is scene-specific and currently powers three separate match-time layers in `polbots`:

- `Lines`: live event narration text and residue
- `InterruptSeeds`: short pre-generated reaction seeds for injected micro-chats
- announcer settings: optional TTS playback for narrated event lines

Example:

```json
{
  "Type": "soccer",
  "MatchTimeLimit": 10,
  "TimeBetweenGames": 0,
  "MaxVolume": 1.0,
  "EnableAnnouncer": true,
  "AnnouncerVolume": 0.9,
  "MaxAnnouncerQueue": 4,
  "SkipAnnouncerDuringInterrupts": false,
  "AnnouncerVoice": "alloy",
  "ClearSceneOnGameEnd": true,
  "RequireTextPatternMatch": false,
  "GameOnStart": false,
  "GameOnBatchEnd": false,
  "GameOnMatchEnd": false,
  "Lines": {
    "FirstWhistleEvent": [
      "The whistle blows.",
      "And we're off.",
      "Kickoff."
    ],
    "FinalWhistleEvent": [
      "# That's the final whistle! {1}",
      "# Game over! {1}",
      "# And that's full-time! {1}"
    ],
    "RefereeShortWhistleEvent": [
      "Quick whistle!",
      "What's the call?",
      "The ref steps in!"
    ],
    "BallHitTheWoodWorkEvent": [
      "SO CLOSE!",
      "Denied by the post!",
      "Off the bar!"
    ],
    "PlayerSlideTackleEvent": [
      "{0} lunges in!",
      "{0} goes for the slide tackle!",
      "{0} clatters into them!"
    ]
  },
  "InterruptSeeds": {
    "Pregame": [
      "Kickoff is approaching. Deliver a very short pregame beat about nerves, legitimacy, betting chatter, or alliance rivalry before play starts.",
      "The match is about to begin. Deliver a very short pregame beat about diplomatic pageantry, procedural anxiety, or quiet panic before kickoff."
    ],
    "GoalScoredEvent": [
      "A goal has just been scored. React briefly and sharply without naming a specific score or minute.",
      "A goal has changed the atmosphere instantly. Deliver a short reaction about humiliation, momentum, or political overreaction without citing exact numbers."
    ],
    "RefereeShortWhistleEvent": [
      "The referee has interrupted play. Deliver a brief reaction about procedure, officiating, corruption, or legitimacy.",
      "A quick whistle just cut through the match. React briefly to the call, the process, or immediate suspicion of bias."
    ],
    "KeeperSavesTheBallEvent": [
      "A dramatic save just happened. React briefly without naming a specific score or minute.",
      "The keeper just denied what looked inevitable. Deliver a short reaction about survival, theft, or divine intervention without exact numbers."
    ],
    "BallHitTheWoodWorkEvent": [
      "A near miss just rattled the stadium. React briefly without naming a specific score or minute.",
      "The ball hit the woodwork and everything nearly changed. Deliver a short reaction about fate, robbery, or nerves without exact numbers."
    ],
    "PlayerSlideTackleEvent": [
      "A hard tackle just changed the emotional temperature of the match. Deliver a brief reaction.",
      "A heavy slide tackle just landed. React briefly about aggression, legitimacy, revenge, or selective outrage."
    ]
  }
}
```

Notes:

- `Lines` are short event phrases used for live narration, residue, and the optional announcer voice layer.
- `InterruptSeeds` are not dialogue output; they are brief generation seeds used to prebuild character reaction packets.
- Keep `InterruptSeeds` generic enough that pre-generated packets do not go stale immediately when the score changes.
- Full-scene soccer framing now lives in Vault under [`Vault/polbots/Prompts/Soccer Mode/Idea Seeds`](C:/Users/akriv/OneDrive/Desktop/HBOx/Vault/polbots/Prompts/Soccer%20Mode/Idea%20Seeds).

## Replay and generation data

Generated chats are serialized to the user's Documents folder under a per-show directory:

- `Documents/<ShowName>/<chat-slug>.json`

Those replay files can later be reloaded by the folder replay source and mixed back into the queue.

Prompt inputs and outputs are also written under `Vault/<ShowName>/Inputs/...` and `Vault/<ShowName>/Outputs/...` as generation runs.

## Development notes

- `ChatManagerContext` defines a show's cast, sentiments, spawn points, audio, config manager, and scene identity.
- Scene-specific scripts should generally subscribe to core events instead of replacing the playback pipeline.
- If a show only needs different actors, prefabs, prompts, and locations, it can usually be built without adding new scene-side C#.

## Legacy note

Older docs, file names, and comments may still refer to `polbots`. In the current project, that is just one show/context inside the larger HBOx runtime.
