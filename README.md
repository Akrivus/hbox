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
    "SlowModel": "gpt-5.4",
    "FastModel": "gpt-5.4-mini",
    "ModelProfiles": {
      "Utility": "gpt-5.4-nano",
      "PostProcess": "gpt-5.4-mini",
      "Sentiment": "gpt-5.4-nano",
      "Dialogue": "gpt-5.4",
      "SceneReasoning": "gpt-5.4"
    },
    "ModelPrices": {
		  "gpt-5.4": {
		    "InputPerMillion": 2.50,
		    "CachedInputPerMillion": 0.25,
		    "OutputPerMillion": 4.50
		  },
		  "gpt-5.4-mini": {
		    "InputPerMillion": 0.20,
		    "CachedInputPerMillion": 0.02,
		    "OutputPerMillion": 1.25
		  },
		  "gpt-5.4-nano": {
		    "InputPerMillion": 0.75,
		    "CachedInputPerMillion": 0.02,
		    "OutputPerMillion": 1.25
		  },
		  "text-embedding-3-small": {
		    "InputPerMillion": 0.02,
		    "CachedInputPerMillion": 0,
		    "OutputPerMillion": 0
		  },
		  "gpt-4o-mini-tts": {
		    "InputPerMillion": 0.60,
		    "CachedInputPerMillion": 0,
		    "OutputPerMillion": 12.00
		  },
		  "google-standard-tts": {
		    "InputPerMillion": 0.000004,
		    "CachedInputPerMillion": 0,
		    "OutputPerMillion": 0
		  }
    },
    "PersistUsage": true,
    "UsageLogPath": "Logs/llm-usage",
    "Budgets": {
      "DailyUsd": 5.0,
      "PerEpisodeUsd": 0.25,
      "WarnAtPercent": 80,
      "EnableUiWarnings": true,
      "EnableDiscordWarnings": false,
      "DiscordChannel": "#stream"
    },
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
    "RequestSpacingSeconds": 6,
    "RateLimitCooldownSeconds": 300,
    "MaxRequestAttempts": 3,
    "OAuthClientId": "YOUR_REDDIT_APP_CLIENT_ID",
    "OAuthClientSecret": "YOUR_REDDIT_APP_CLIENT_SECRET",
    "OAuthDeviceId": "DO_NOT_TRACK_THIS_DEVICE",
    "OAuthUserAgent": "script:hbox:1.1 (by /u/YOUR_REDDIT_USERNAME)",
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
- `reddit.BatchPeriodOffset` and `reddit.BatchPeriodInMinutes` control Reddit fetch cadence all day; `reddit.ActiveHoursStart` and `reddit.ActiveHoursEnd` only gate queueing/autoapproval work.
- Pitch votes resolve after the voting window closes: more thumbs-up than thumbs-down queues the pitch as an `Idea`; more thumbs-down rejects it; ties or too few votes expire.
- `reddit.PitchExpirationMinutes` is how long a posted pitch can receive approval votes before it is treated as stale.
- `reddit.PitchAutoApprovalBatchSize` is how many top evaluator-approved Reddit pitches can skip Discord voting and queue directly per active Reddit batch slot. Set it to `0` to require voting for every pitch.
- `reddit.PitchMinimumVotesToQueue` is the minimum total vote count required before an expired positive voted pitch can queue.
- Autoapproved pitch candidates are sorted by Reddit comment count weighted most heavily, then Reddit karma, then newest post first.
- Pitch generation uses `Vault/{show}/Prompts/Reddit Source/Pitch Candidate.md`, then `Vault/{show}/Prompts/Reddit Source/Pitch Evaluator.md` to reject weak or overlong pitches before posting.
- Posted pitch cards surface the pitch, cast, source subreddit, Reddit karma/comment counts, and evaluator approval reason; the approved `Idea` still includes the raw Reddit post text and mined thread material for generation context.
- In pitch-gate mode, Reddit posts are only written to the local seen history after a pitch is accepted for voting, so evaluator-rejected posts can be reconsidered later.
- `reddit.RequestSpacingSeconds` applies a shared delay across Reddit listing and thread-mining requests; `reddit.RateLimitCooldownSeconds` is used before retrying after Reddit returns 429 or a 403 blocked response; `reddit.MaxRequestAttempts` bounds those retries.
- `reddit.OAuthClientId` enables app-only OAuth for read-only listing and comment requests. Add `OAuthClientSecret` for a confidential/script app, or omit the secret and set `OAuthDeviceId` for an installed-app client. When OAuth is configured, Reddit fetches use `oauth.reddit.com` with a cached bearer token.
- `reddit.OAuthUserAgent` should identify the app and Reddit username that owns the client, for example `script:hbox:1.1 (by /u/yourname)`.
- Approved pitch posts are pinned by the Discord bot when bot permissions allow it, and the operator panel reads `/api/pitches` for the current pitch deck.
- OpenAI text generation and OpenAI TTS are configured separately.
- `UseEmbeddings` enables semantic memory recall. When disabled, memory context falls back to the most recent saved memories.
- Some integrations only matter in specific scenes, such as `soccer` in `polbots`.

### OpenAI model profiles and usage budgets

`SlowModel` and `FastModel` remain the default compatibility models. `ModelProfiles` can override specific LLM call classes without changing older call sites:

- `Slow` and `Fast`: direct replacements for the legacy binary model switch.
- `Utility`, `PostProcess`, and `Sentiment`: cheap/default profiles for low-reasoning work.
- `Dialogue` and `SceneReasoning`: higher-capability profiles for chain stages that benefit from stronger reasoning.

`ModelPrices` is keyed by model id. Exact model ids are matched first; versioned response ids such as `gpt-5.4-mini-03-26` fall back to the longest configured prefix such as `gpt-5.4-mini`. The values in the example above are illustrative; update them when model pricing changes. Costs are calculated from response usage metadata when available:

- `InputPerMillion`: uncached input token price.
- `CachedInputPerMillion`: cached input token price.
- `OutputPerMillion`: output token price.

Embeddings and TTS use the same `ModelPrices` table so the dashboard can show them in the budget widget without a separate cost pipeline. For embeddings, `InputPerMillion` is applied to estimated input tokens. For TTS, `InputPerMillion` is applied to input characters, so configure model ids such as `gpt-4o-mini-tts` and `google-standard-tts` with per-1M-character prices if you want speech cost included.

Current standard API prices for the main text generation models, in USD per 1M tokens:

| Model | InputPerMillion | CachedInputPerMillion | OutputPerMillion | Notes |
| --- | ---: | ---: | ---: | --- |
| `gpt-5.5` | 5.00 | 0.50 | 30.00 | Short-context standard price. Long-context pricing is higher. |
| `gpt-5.5-pro` | 30.00 | 0.00 | 180.00 | No cached input price is listed. Long-context pricing is higher. |
| `gpt-5.4` | 2.50 | 0.25 | 15.00 | Short-context standard price. Long-context pricing is higher. |
| `gpt-5.4-mini` | 0.75 | 0.075 | 4.50 | Cost-efficient general model. |
| `gpt-5.4-nano` | 0.20 | 0.02 | 1.25 | Lowest-cost current GPT-5.4 class model. |
| `gpt-5.4-pro` | 30.00 | 0.00 | 180.00 | No cached input price is listed. Long-context pricing is higher. |
| `chat-latest` | 5.00 | 0.50 | 30.00 | ChatGPT model alias. |
| `gpt-5.3-codex` | 1.75 | 0.175 | 14.00 | Codex-specialized model. |

The runtime currently stores one price row per model id. If a model has separate short-context and long-context prices, use the row that matches the expected workload or split usage into distinct model ids/config aliases before relying on budget totals for exact accounting.

When `PersistUsage` is enabled, usage records are appended as JSONL under `UsageLogPath`, one file per local date. Each call is tagged with usage type, profile, resolved model, prompt/template part, caller type/member, channel key, episode slug, token counts or billable units, cached/reasoning token details, latency, success/error status, and calculated cost.

`Budgets` controls warning thresholds:

- `DailyUsd`: daily cost limit across all LLM calls. Set to `0` to disable daily budget warnings.
- `PerEpisodeUsd`: per-episode cost limit. Set to `0` to disable per-episode budget warnings.
- `WarnAtPercent`: percentage of a configured limit that emits a warning before the hard limit is exceeded.
- `EnableUiWarnings`: publishes budget warnings to the in-app UI event overlay.
- `EnableDiscordWarnings`: posts budget warnings to Discord when Discord webhooks are configured.
- `DiscordChannel`: optional webhook key for budget warnings. If blank, the current stream channel is used.

Budget warnings are also recorded in the operator event stream as `llm_budget_warning`.

LLM usage APIs:

- `GET /api/llm/calls?limit=100`
- `GET /api/llm/summary?groupBy=template`
- `GET /api/llm/summary?groupBy=ip`
- `GET /api/llm/summary?groupBy=generator`
- `GET /api/llm/summary?groupBy=model`
- `GET /api/llm/summary?groupBy=profile`
- `GET /api/llm/budget?limit=1000`
- `GET /api/llm/usage?range=day`
- `GET /api/llm/usage?range=week`
- `GET /api/llm/usage?range=month`
- `GET /api/llm/usage?range=all`
- `GET /api/llm/usage/calls?range=week&limit=24`
- `GET /api/llm/history/calls?date=YYYY-MM-DD`
- `GET /api/llm/history/budget?date=YYYY-MM-DD`

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
