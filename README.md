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
    "WebhookURLs": {
      "#stream": "YOUR_STREAM_WEBHOOK_URL",
      "#sports": "YOUR_SPORTS_WEBHOOK_URL"
    },
    "AvatarURL": "https://example.com/avatar.png"
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
- OpenAI text generation and OpenAI TTS are configured separately.
- Some integrations only matter in specific scenes, such as `soccer` in `polbots`.

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
