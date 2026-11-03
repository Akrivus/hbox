# HBOx

HBOx is a Unity project for generating and replaying AI-driven "Chats": short staged scenes with a cast, location, dialogue, sentiment, timing, and audio. The project started as `polbots`, but it now acts more like a shared runtime for multiple shows and worlds.

The current project is split into two main layers:

- `Assets/Core`
  Shared engine code for chat generation, playback, actor control, UI, config loading, external integrations, replay loading, and prompt resolution.
- `Assets/Scenes`
  Show-specific scenes, actors, prefabs, audio, and scene adapters.

## What is defined here and what is generated

The repository defines the Unity runtime, prompts in `Vault`, show-specific casts, scenes and assets, configuration, and systems for sourcing ideas, staging, playback, and interactions such as `polbots` soccer. These provide the structure and material from which each chat is made.

At runtime, an LLM uses an `Idea` and those prompts to generate a `Chat`, including topic, cast and location choices, dialogue, reactions, voice lines, and related metadata. `ChatManager` and `ActorController` stage and perform that generated material with the repository's actors and assets. Chats can also be saved as JSON and replayed.

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

The project loads shared settings from `hbox.json` and overlays the active channel's config file through `ConfigManager`.

The shared `channels` entry controls which channels are loaded during startup. Only listed channel keys are added to the guide and warmed into memory. If the entry or `Enabled` property is omitted, all channels remain enabled; an empty list enables none. Changes take effect the next time HBOx starts.

```json
{
  "Type": "channels",
  "Enabled": ["polbots", "romebots", "spacedrivel", "appydays"]
}
```

Each config entry must contain a `Type` field. Only the systems present in your scene/context need to be configured.

### Control panel hostname and port

The HTTP server reads a machine-wide `server` entry from `hbox.json` at startup (channel configs do not override it). Add this object to the existing JSON array to use `http://hbox.local`:

```json
{
  "Type": "server",
  "Port": 80,
  "EnableMdns": true,
  "HostName": "hbox"
}
```

Without this entry the port remains **6789**, mDNS is enabled in both player builds and the Unity editor, and the hostname is `hbox.local`. For local development alongside another instance on the network, set a different `HostName` or set `EnableMdns` to `false`. With a port other than 80, include it in the URL, for example `http://hbox.local:6789`. Localhost and the selected LAN IPv4 address remain available. `/api/health` reports the configured localhost URL.

`HostName` is a single DNS label without `.local`. Machines sharing a LAN must use distinct names, such as `hbox-studio` and `hbox-office`. Different networks can each use `hbox`. The advertiser probes for conflicting names before publishing an IPv4 address and an `_http._tcp.local` service with the configured port. If it detects a conflict, discovery stops with a warning while HTTP keeps running. Choose another name and restart. Settings take effect on restart.

mDNS requires multicast between the client and host on the same network. Allow the player through the firewall on trusted local networks for UDP 5353 and the configured TCP port. On Windows, HTTP.sys may also require URL reservations for the account running HBOx. If startup reports access denied, run these in an elevated PowerShell, substituting the actual LAN IP, hostname and port:

```powershell
$account = "$env:USERDOMAIN\$env:USERNAME"
netsh http add urlacl url=http://localhost:80/ user="$account"
netsh http add urlacl url=http://192.168.1.50:80/ user="$account"
netsh http add urlacl url=http://hbox.local:80/ user="$account"
```

The chosen port must be free; startup errors are logged rather than silently selecting another port.

Run the standalone discovery/config regression harness with `dotnet run --project Tests/Server/ServerTests.csproj`. Append `-- --network` to also test a temporary unique hostname over real UDP mDNS and verify shutdown.

### Configuration example

```json
[
  {
    "Type": "openai",
    "ApiUri": "https://api.openai.com",
    "ApiKey": "YOUR_OPENAI_API_KEY",
    "SlowModel": "gpt-6.1-sol",
    "FastModel": "gpt-5.6-terra",
    "ModelProfiles": {
      "Utility": "gpt-5.6-terra",
      "PostProcess": "gpt-5.6-terra",
      "Sentiment": "gpt-5.6-terra",
      "Dialogue": "gpt-6.1-sol",
      "SceneReasoning": "gpt-6.1-sol"
    },
    "ModelPrices": {
      "gpt-6.1-sol": {
        "InputPerMillion": 2.0,
        "CachedInputPerMillion": 0.1,
        "OutputPerMillion": 10.0
      },
      "gpt-6-luna": {
        "InputPerMillion": 0.1,
        "CachedInputPerMillion": 0.01,
        "OutputPerMillion": 0.5
      },
      "text-embedding-3-small": {
        "InputPerMillion": 0.02,
        "CachedInputPerMillion": 0,
        "OutputPerMillion": 0
      },
      "gemini-2.5-flash-tts": {
        "InputPerMillion": 0.5,
        "CachedInputPerMillion": 0,
        "OutputPerMillion": 10.0
      },
      "gemini-2.5-pro-tts": {
        "InputPerMillion": 1.0,
        "CachedInputPerMillion": 0,
        "OutputPerMillion": 20.0
      },
      "gpt-4o-mini-tts": {
        "InputPerMillion": 0.6,
        "CachedInputPerMillion": 0,
        "OutputPerMillion": 12.0
      },
      "google-standard-tts": {
        "InputPerMillion": 4.0,
        "CachedInputPerMillion": 0,
        "OutputPerMillion": 0
      },
      "gemini-3.1-flash-tts-preview": {
        "InputPerMillion": 1.0,
        "CachedInputPerMillion": 0,
        "OutputPerMillion": 20.0
      },
      "gpt-5.6-sol": {
        "InputPerMillion": 4.0,
        "CachedInputPerMillion": 0.4,
        "OutputPerMillion": 20.0
      },
      "gpt-5.6-terra": {
        "InputPerMillion": 2.0,
        "CachedInputPerMillion": 0.2,
        "OutputPerMillion": 12.0
      },
      "gpt-5.6-luna": {
        "InputPerMillion": 0.2,
        "CachedInputPerMillion": 0.02,
        "OutputPerMillion": 1.2
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
    "OpenAiApiKey": "YOUR_OPENAI_TTS_KEY",
    "GoogleGeminiModel": "gemini-3.1-flash-tts-preview"
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
    "SlashCommandGuildIds": [
      "YOUR_TEST_GUILD_ID"
    ],
    "EnableIdeaCommand": true,
    "DefaultDailyIdeaLimit": 3,
    "BoosterDailyIdeaLimit": 10,
    "BoosterRoleIds": [
      "BOOSTER_ROLE_ID"
    ],
    "AdminDailyIdeaLimit": 30,
    "AdminRoleIds": [],
    "AdminUserIds": []
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
    "EnablePitchGate": true,
    "PitchAutoApprovalBatchSize": 0,
    "Enabled": true,
    "SubReddits": {
      "worldnews+anime_titties+todayilearned": "Default",
      "AskHistorians+UnitedNations+geopolitics": "History",
      "Africa+China+america+australia+europe": "Regional"
    },
    "MaxPostAgeInHours": 24,
    "Batch": {
      "BatchSize": 2,
      "BatchSizeLimit": 2,
      "BatchIterations": 1,
      "BatchPeriodOffset": "00:05:00",
      "BatchPeriodInMinutes": 5,
      "ActiveHoursStart": "00:00",
      "ActiveHoursEnd": "23:59"
    },
    "PitchDiscordChannel": "#stream",
    "PitchExpirationMinutes": 180,
    "PitchApprovalScore": 2,
    "PitchMinimumVotesToQueue": 1,
    "PitchMemoryMaxChars": 4000,
    "PitchContinuityEpisodeLimit": 6,
    "PitchContinuityMaxChars": 4000,
    "PitchSourceSelftextMaxChars": 1200,
    "PitchEvaluatorThreadMaxChars": 1800,
    "PitchEvaluatorMemoryMaxChars": 1200,
    "PitchEvaluatorContinuityMaxChars": 1200,
    "PitchEvaluatorActorLimit": 40,
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
    "Type": "polymarket",
    "EnablePitchGate": true,
    "PitchAutoApprovalBatchSize": 0,
    "Enabled": true,
    "TagSlugs": ["politics"],
    "DiscoveryPageSize": 50,
    "DiscoveryPages": 2,
    "MinLiquidity": 1000,
    "MinVolume24hr": 1000,
    "RepeatCooldownHours": 24,
    "RequestSpacingSeconds": 1,
    "MaxRequestAttempts": 3,
    "Batch": {
      "BatchSize": 5,
      "BatchSizeLimit": 2,
      "BatchIterations": 1,
      "BatchPeriodOffset": "00:02",
      "BatchPeriodInMinutes": 30,
      "ActiveHoursStart": "00:00",
      "ActiveHoursEnd": "23:59"
    },
    "PitchDiscordChannel": "#stream",
    "PitchExpirationMinutes": 180,
    "PitchApprovalScore": 2,
    "PitchMinimumVotesToQueue": 1
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

### Web source settings

Reddit and Polymarket run independently through the shared `WebSource` pipeline. Keep scheduling fields inside each source's `Batch` block; provider settings and shared `Pitch*` settings stay outside it. The examples above allow up to two Reddit pitches every five minutes and two Polymarket pitches every thirty minutes, with viewer voting required for both.

| Setting | Behavior |
| --- | --- |
| `Batch.BatchSize` | Documents requested per adapter pass, and maximum passes per iteration. Reddit advances through subreddits; Polymarket rotates topic tags. |
| `Batch.BatchSizeLimit` | Strict total accepted pitches or direct ideas per drop, including automatically approved pitches. Rejections do not consume the limit. |
| `Batch.BatchIterations` | Number of rounds of adapter passes. Each source document is evaluated at most once per drop. |
| `Batch.BatchPeriodOffset` / `Batch.BatchPeriodInMinutes` | Local-time schedule aligned from midnight plus the offset. The period must be between one second and one year. |
| `Batch.ActiveHoursStart` / `Batch.ActiveHoursEnd` | Window permitting automatic approval. Discovery and viewer voting continue outside it; overnight windows are supported. |
| `Enabled` | Set `false` to stop intake when reloading configuration. Removing a source block disables it on the next startup. |
| `LogoUrl` / `BrandColor` | Optional voting-embed logo URL and integer color overrides. Defaults are Reddit orange (`16729344`, `#FF4500`) and Polymarket blue (`3038463`, `#2E5CFF`). |

Legacy flat batch fields remain supported. When `Batch` is present, it replaces the flat batch policy entirely; omitted nested fields use shared defaults, not the flat values. Those defaults are size/limit `20`, iterations `1`, period `60` minutes, offset `00:00`, and active hours `00:00`–`23:59`.

Both sources accept the same `Pitch*` settings, including memory/evaluator limits. `EnablePitchGate` is configurable and defaults to `true`. Set it to `false` for direct source-to-idea delivery during local testing, without pitch generation, evaluation or voting. With the gate enabled, `PitchAutoApprovalBatchSize` allows up to that many evaluator-approved pitches per active batch to skip voting; `0` requires viewer votes for every approved pitch. Rejected pitches never auto-approve. Overflow goes to voting, and active-hour settings limit automatic approvals. Both controls persist in config. The local server manual endpoint remains direct, and Discord commands/channel messages retain their shared submission pipeline.

Polymarket-specific discovery settings:

| Setting | Default | Behavior |
| --- | --- | --- |
| `TagSlugs` | `["politics"]` | Topic feeds rotated during discovery; multiple tags are separate feeds, not an intersection. An empty list includes all topics. |
| `DiscoveryPageSize` | `50` | Events fetched per API page; allowed range is 1–100. |
| `DiscoveryPages` | `2` | Maximum pages scanned per adapter pass; allowed range is 1–20. |
| `MinLiquidity` / `MinVolume24hr` | `1000` each | Minimum liquidity and 24-hour trading volume on the selected market. |
| `RepeatCooldownHours` | `24` | Suppresses accepted event IDs until the cooldown passes; a later snapshot gets a new voting window. |
| `RequestSpacingSeconds` | `1` | Minimum spacing between discovery requests. |
| `MaxRequestAttempts` | `3` | Bounded retries for rate limits and transient failures; allowed range is 1–5. |

Polymarket reads public Gamma data and requires no API key or wallet. It selects at most one eligible open market per event. Voting cards show outcome prices, liquidity, recent volume and the snapshot time. The adapter retains resolution rules and treats prices as market-implied expectations. Accepted-event history is saved in `polymarket-{show}.json`; both sources share the existing `reddit-pitches-{show}.json` pitch manifest.

Edit Polymarket prompts in Obsidian at `Vault/{show}/Prompts/Polymarket Source.md`, `Polymarket Source/Pitch Candidate.md`, and `Polymarket Source/Pitch Evaluator.md`. These files are read for each generation and must be copied with the locally managed Vault when moving the setup. See [web sources and voting](Docs/WebSources.md) for the full behavior and prompt argument reference.

### Notes

- Discord replay and pitch reactions count as **3 votes for administrators, 2 for boosters, and 1 for everyone else** (the highest tier wins). Both thumbs-up and thumbs-down use these weights, including pitch minimum-vote and approval-score thresholds. Discord's visible reaction counter still counts people, while HBOx stores weighted totals. Super-reactions do not add extra votes.
- Administrators are detected from server ownership or roles with Discord's Administrator permission. `discord.AdminRoleIds` can designate additional roles, and `discord.AdminUserIds` can designate individual accounts for solo testing. Use ID strings, not display names. Boosters are detected from Discord boost membership/role metadata or the existing `BoosterRoleIds` list.
- `/idea` uses the same tier detection: `DefaultDailyIdeaLimit` defaults to 3, `BoosterDailyIdeaLimit` to 10, and `AdminDailyIdeaLimit` to 30. Higher tiers never receive a lower quota than lower tiers. Quotas reset at local midnight and remain in memory, as before.
- `/idea generator:<slug> prompt:<text>` accepts optional `image`, `image2`, `image3` and `image4` uploads plus `context_only:true`. PNG/JPEG files may be up to 2 MiB each; shared dimension limits apply. The bot privately acknowledges uploads, caches valid bytes, and submits the source to `DiscordSubmissionIntake` for the same Vault interpretation, evaluation and voting used by channel messages. The receipt confirms admission for review, not generation. Invalid uploads or failed admission consume no quota; an admitted command counts once even if its pitch is later rejected. Text-only submissions receive an immediate review receipt. Restart/reconnect the bot to refresh the slash-command description. This uses Discord's [attachment command options](https://docs.discord.com/developers/interactions/application-commands) and [deferred interaction responses](https://docs.discord.com/developers/interactions/receiving-and-responding).
- Reaction weights are saved in `hbox-discord-votes.json` under Unity's persistent data directory. Removing a tracked reaction reverses its original weight, even after a role change or restart; repeated add/remove deliveries are ignored. Existing votes are not retroactively reweighted, and untracked legacy removals subtract 1. Keep this file with the replay/pitch manifests when retaining vote history. Gateway events missed while the bot is offline are not reconciled.

- For web sources, remove a config block to disable it on the next startup, or set `Enabled: false` when reloading configuration.
- `folder` is the active replay loader type registered at runtime.
- `reddit.SubReddits` is a dictionary, not a simple string array.
- `reddit.EnablePitchGate: true` uses `RedditSource -> PitchCandidate -> approval/vote -> Idea`; `false` enables direct source-to-idea delivery for testing. Polymarket can run alongside Reddit through the shared `WebSource` batch/pitch pipeline; see [web source configuration](Docs/WebSources.md) for independent batch policies, source-branded voting cards and compatibility details.
- `reddit.PitchDiscordChannel` must match a configured Discord webhook key, usually `#stream`.
- `reddit.Batch.BatchPeriodOffset` and `reddit.Batch.BatchPeriodInMinutes` control fetch cadence. `ActiveHoursStart`/`ActiveHoursEnd` limit automatic approvals; discovery and voting continue outside the window. Polymarket uses the same nested settings independently.
- Pitch votes queue immediately once the live vote score reaches `reddit.PitchApprovalScore` and the total vote count meets `reddit.PitchMinimumVotesToQueue`. Remaining posted pitches are resolved after the voting window closes: enough positive votes queue the pitch as an `Idea`; more thumbs-down rejects it; ties, too few votes, or positive scores below the approval score expire.
- `reddit.PitchExpirationMinutes` is how long a posted pitch can receive approval votes before it is treated as stale.
- `reddit.PitchApprovalScore` is the positive vote score required to queue a live or expired pitch.
- `reddit.PitchAutoApprovalBatchSize` sets the number of evaluator-approved pitches that can skip voting per active batch. The default `0` requires voting for all approved pitches.
- `reddit.PitchMinimumVotesToQueue` is the minimum total vote count required before a positive voted pitch can queue.
- `reddit.PitchMemoryMaxChars`, `reddit.PitchContinuityEpisodeLimit`, `reddit.PitchContinuityMaxChars`, and `reddit.PitchSourceSelftextMaxChars` bound the context used while writing pitch cards.
- `reddit.PitchEvaluatorThreadMaxChars`, `reddit.PitchEvaluatorMemoryMaxChars`, `reddit.PitchEvaluatorContinuityMaxChars`, and `reddit.PitchEvaluatorActorLimit` keep the rejection filter cheap; rejected candidates no longer need to pay for generation-sized context.
- Reddit pitch candidates are ranked by comment count weighted most heavily plus the square root of karma, then newest post first.
- Pitch generation uses `Vault/{show}/Prompts/Reddit Source/Pitch Candidate.md`, then `Vault/{show}/Prompts/Reddit Source/Pitch Evaluator.md` to reject weak or overlong pitches before posting.
- Posted pitch cards surface the pitch, cast, branded source label/logo/color, provider-specific metrics, and evaluator approval reason; the approved `Idea` retains raw source context. Reddit cards show subreddit/karma/comments; Polymarket cards show timestamped outcome prices, liquidity and 24-hour volume.
- In pitch-gate mode, Reddit posts are only written to the local seen history after a pitch is accepted for voting, so evaluator-rejected posts can be reconsidered later.
- `reddit.RequestSpacingSeconds` applies a shared delay across Reddit listing and thread-mining requests; `reddit.RateLimitCooldownSeconds` is used before retrying after Reddit returns 429 or a 403 blocked response; `reddit.MaxRequestAttempts` bounds those retries.
- `reddit.OAuthClientId` enables app-only OAuth for read-only listing and comment requests. Add `OAuthClientSecret` for a confidential/script app, or omit the secret and set `OAuthDeviceId` for an installed-app client. When OAuth is configured, Reddit fetches use `oauth.reddit.com` with a cached bearer token.
- `reddit.OAuthUserAgent` should identify the app and Reddit username that owns the client, for example `script:hbox:1.1 (by /u/yourname)`.
- Approved pitch posts are pinned by the Discord bot when bot permissions allow it, and the operator panel reads `/api/pitches` for the current pitch deck.
- Expired pitch polls delete their original Discord webhook message after the vote is resolved.
- Now Playing Discord webhook messages are deleted when the episode finishes.
- OpenAI text generation and OpenAI TTS are configured separately.
- `UseEmbeddings` enables semantic memory recall. When disabled, memory context falls back to the most recent saved memories.
- Some integrations only matter in specific scenes, such as `soccer` in `polbots`.

### Source image attachments

Reddit image context is enabled by default; set `reddit.EnableImageContext` to `false` to disable new image collection. Image posts, ordered galleries and crossposts can supply up to four PNG/JPEG attachments per idea. Article thumbnails, video previews, animated gallery items, spoilers and NSFW images are omitted. Image downloads are limited to 2 MiB each, 8192 pixels per dimension and 16 million pixels total; oversized images are omitted rather than resized.

`WebSourceDocument.images` and `Idea.Images` share the `ImageAttachment` model. Each attachment retains its ID, image URL, source link/label, caption, MIME type, embedded bytes, hash, retrieval time and cached analysis. Pitch generation analyzes images before creating/evaluating the pitch; direct ideas prepare them before episode generation. Analysis uses the configured `SceneReasoning` model, which must support image input. Readable text and visible details become bounded source context for pitch evaluation and subsequent generation stages. Image bytes are sent to the model for analysis once per attachment; they are excluded from text prompts. Screenshot claims remain source claims rather than verified reporting. The image-input format follows the [OpenAI vision documentation](https://developers.openai.com/api/docs/guides/images-vision?api-mode=chat).

Attachments persist with pitch records and in the saved Chat's `Idea.Images`, keeping replay assets usable without the original image host. Existing pitches and replays without images continue to load. Failed downloads/analysis record `DownloadError`/`AnalysisError` and allow text generation to continue; clear the corresponding error to retry preparation. Successful bytes remain available even if analysis fails.

`UseForGeneration` defaults to true and `PresentationEligible` to false. Images start unapproved for display; PBNN/polbots presentation planning can approve selected assets after dialogue generation. `ContextOnly: true` is an explicit veto that both the planner and playback enforce. Use `/idea context_only:true` to apply it to all command uploads, or set `ImagesContextOnly: true` on a submissions-channel route. These controls do not prevent image context from informing generation. Commands and channel messages cache source images before pitch review and carry them into `Idea.Images` only after a successful vote, so generation and replay do not depend on Discord's temporary attachment URLs.

Image prompts follow the normal per-show Vault workflow: edit `Vault/{show}/Prompts/Source Images/Analysis.md` for vision instructions and `Vault/{show}/Prompts/Source Images/Context.md` for the wording around cached context. Starter files live in `Docs/Prompts/Source Images`; copy that folder into a new show's `Prompts` directory. There are no inline prompt fallbacks. Analysis placeholders are `{0}` attachment ID, `{1}` source label, `{2}` caption and `{3}` source link; the context template receives formatted attachment data as `{0}`. Analysis inputs/outputs and context inputs are saved under the show's normal Vault folders. Existing cached analyses remain until their `Analysis` value is cleared; clear `AnalysisError` as well when retrying a failed analysis after a prompt edit.

Run `Tests/WebSources/Run.ps1` for image discovery, preparation, failure handling, pitch/direct queue delivery and production Chat JSON replay regression checks.

### In-scene image presentation

`ChatGenerator.OnChatGenerated` is an awaited scene-extension event: it runs after all dialogue/subgenerators complete and before locking/saving the Chat. The authored `SceneImagePresentation` components in PBNN and polbots subscribe to this event and run `Vault/{show}/Prompts/Source Images/Presentation.md`. Core has no channel whitelist or automatic show-component bootstrap. The editable prompt receives the original idea, topic, bounded image descriptions/provenance, indexed final dialogue and scene-registered displays, and selects images and inclusive start/exclusive end node ranges. Unselected images remain context-only for that episode. Empty cue lists are valid; image-selection instructions remain in Vault.

The resulting `scene.images.v1` plan lives in `Chat.GeneratedData` and references `Idea.Images` by ID; it does not duplicate binary assets. Only validated cues authorize `PresentationEligible`. Unknown IDs/surfaces, overlapping cues on one surface, invalid ranges, missing analysis/attribution/bytes, and hard context-only images are rejected. Invalid or missing presentation prompts/model output leave the episode without attachment visuals, while dialogue generation can still finish. Playback uses the saved plan and bytes without network requests or further model calls. `Presentation.md` edits affect newly generated plans; existing replays keep their saved decisions.

`SceneImagePresentation.OnCollectSurfaces` lets scene adapters register `ISceneImageSurface` implementations. Core only handles validated plans, attachment lookup, texture lifetime and existing ChatManager playback events. PBNN's `PbnnMediaPresentation` registers its TV (`news-tv`) and owns the caption/source overlay and legacy fixture handling. `PolbotsImagePresentation` registers `media-screen` and owns media-panel binding, letterboxing and soccer priority. Disabling adapters unregisters them; disabling the shared controller unsubscribes generation/playback handlers. Gaps between cue ranges clear the display, and completion, skip, channel switches or teardown restore the previous surface state.

For future mounts or room displays in any show, author a `SceneImagePresentation` beside that scene's context and add `SceneImageSurface` components with unique IDs/descriptions. Bind either a renderer/material slot and image shader, or a UI RawImage; optional TMP caption/attribution targets can be assigned. For custom behavior, register an `ISceneImageSurface` through `OnCollectSurfaces` and unsubscribe on disable, as the existing scene adapters do. The surface's `CanDisplay(Chat)` owns display-priority policy. No new show requires Core changes. Duplicate IDs are excluded; geometry and camera staging remain authored scene work.

The starter presentation prompt is in `Docs/Prompts/Source Images/Presentation.md`; show-specific copies are installed for PBNN and polbots. Use **HBOx → Images → Validate Scene Image Bindings** in Unity to inspect the authored TV/media-screen references and resource shader. `Tests/PBNN/Run.ps1` exercises shared plan authorization, cue boundaries, replay persistence and context-only/invalid-output rejection alongside legacy media checks.

### Discord submissions channels

Add `SubmissionChannels` to the target show's existing `discord` config entry. The key is the incoming Discord channel ID; no mapping means no submissions intake for that show. Each mapping names a generator and, optionally, a separate pitch-poll channel. Routes from loaded shows are shared with the existing enabled bot, so PBNN does not need a second bot session or webhooks. An enabled bot must be loaded for intake to work.

There is one pitch interpretation/evaluation engine: `PitchCandidateService`, shared by Reddit, Polymarket and Discord. `DiscordSubmissionIntake` is the Discord delivery coordinator: it deduplicates received messages, calls that shared engine, publishes accepted candidates through the normal pitch gate, and maintains poll expiry. It does not have separate pitch-writing or evaluation logic. Transport, source history and scheduling stay with each intake adapter; voting, idea conversion and persistence stay in `PitchCandidateStore`.

`/idea` uses this same intake. It inherits the target generator's configured submission prompt root, poll destination, image veto and vote thresholds. Without a matching channel route it uses the standard `Discord Submissions` templates and posts polls in the invoking channel. If multiple routes target the same generator, their policies must agree; conflicting policies reject the command rather than silently selecting a weaker gate. Command quotas and ephemeral upload receipts remain separate admission controls. Settings changes invalidate queued/in-flight commands just as they invalidate channel submissions. Command identities are stable `discord:command:<interaction ID>` source keys; accepted replay filenames are normalized separately so identity punctuation cannot create invalid Windows filenames.

```json
"SubmissionChannels": {
  "1566526475971207289": {
    "Enabled": true,
    "GeneratorSlug": "pbnn-default",
    "PitchChannelId": "1566526572335333537",
    "PromptRoot": "Discord Submissions",
    "PitchExpirationMinutes": 180,
    "PitchMinimumVotesToQueue": 1,
    "PitchApprovalScore": 1
  }
}
```

This mapping is installed in the local `pbnn.json`. Omitting `PitchChannelId` posts polls in the incoming submissions channel. Set `Enabled: false` or remove a mapping to disable it. Only new ordinary messages and replies in explicitly mapped channels are ingested. Bot/webhook/system messages are ignored; edits, deletions, historical backfill, link previews and thread children do not create new submissions. Duplicate mappings for the same Discord channel are disabled with a configuration warning.

Enable **Message Content Intent** for the existing bot in the Discord Developer Portal, and grant it access to the submissions and poll channels with permission to send messages, embed links and add reactions. Pinning uses the existing optional bot pin behavior. The listener requests Guild Messages and Message Content only when submissions routes exist and reconnects when those requirements change. Discord documents [message-content intent requirements](https://docs.discord.com/developers/events/gateway#message-content-intent), including access to attachment fields.

Intake caches supported PNG/JPEG uploads using the shared limits, then dispatches pitch work to Unity's main thread. Text-only and image-only suggestions are supported. Unsupported attachments are noted as unavailable; a usable message can still be evaluated. Discord permissions, moderation and slowmode govern channel intake; `/idea` daily quotas do not apply.

The source is interpreted by `Vault/{show}/Prompts/Discord Submissions/Pitch Candidate.md`, then reviewed by `Pitch Evaluator.md` in the same folder. Both files are editable and contain no built-in show, genre or IP assumptions. Starter templates live under `Docs/Prompts/Discord Submissions`. They use the same placeholder order as the web-source pitch prompts: source, attachment notes, cast, memory, UTC time, then continuity for the candidate; the evaluator receives the proposed pitch at `{5}` and continuity at `{6}`. Image analysis continues to use `Source Images/Analysis.md`.

Evaluator-approved submissions are posted as bot messages in the poll channel and saved in the existing pitch manifest. They require the configured vote thresholds before queueing as attributed ideas with their image assets. Rejected submissions never become generation jobs. Polls expire on a timer even without new submissions or web-source batches, and expired bot polls are deleted using their originating bot application. The per-generator `discord-submissions-*.json` history suppresses message redelivery after reconnect/restart, including evaluator rejections. Keep it with pitch/replay history when moving an installation. Disabling a route cancels queued/in-flight interpretation before poll publication; already-posted polls retain their normal voting and expiry behavior.

Run `Tests/Discord/Run.ps1`, `Tests/WebSources/Run.ps1` and `Tests/Context/Run.ps1` for intake/transport, gate/persistence/replay and Vault-template checks.

Migration: the legacy JSON key `TipsChannels` remains readable, but saved configs emit `SubmissionChannels`. An explicit `SubmissionChannels` mapping (including an empty one) takes precedence regardless of JSON property order. The old default prompt-root name maps to `Discord Submissions`; custom prompt roots are preserved. Local Vault folders have been renamed with their existing contents. Deduplication loads both `discord-tips-*.json` and `discord-submissions-*.json` and writes the combined history under the new name. Original Discord message/channel IDs and saved source identities stay stable.

### OpenAI model profiles and usage budgets

`SlowModel` and `FastModel` remain the default compatibility models. `ModelProfiles` can override specific LLM call classes without changing older call sites:

- `Slow` and `Fast`: direct replacements for the legacy binary model switch.
- `Utility`, `PostProcess`, and `Sentiment`: cheap/default profiles for low-reasoning work.
- `Dialogue` and `SceneReasoning`: higher-capability profiles for chain stages that benefit from stronger reasoning.

`ModelPrices` is keyed by model id. Exact model ids are matched first; versioned response ids (when supplied by the API) fall back to the longest configured prefix such as `gpt-6.1-sol`. The GPT-5.6/GPT-6 model ids, profile mappings, and prices in the example above and `hbox.example.json` are aligned with the reference table below; keep both examples and the table in sync when updating these models or prices. Costs are calculated from response usage metadata when available:

- `InputPerMillion`: uncached input token price.
- `CachedInputPerMillion`: cached input token price.
- `OutputPerMillion`: output token price.

Embeddings and TTS use the same `ModelPrices` table so the dashboard can show them in the budget widget without a separate cost pipeline. For embeddings, `InputPerMillion` is applied to estimated input tokens. For legacy TTS, `InputPerMillion` is applied to input characters, so configure model ids such as `gpt-4o-mini-tts` and `google-standard-tts` with per-1M-character prices if you want speech cost included. Gemini uses estimated text/audio tokens instead; see [Google Gemini TTS](Docs/GoogleGeminiTTS.md).

Current standard API prices for the main text generation models, in USD per 1M tokens:

| Model | InputPerMillion | CachedInputPerMillion | OutputPerMillion | Notes |
| --- | ---: | ---: | ---: | --- |
| gpt-6.1-sol | 2.00 | 0.10 | 10.00 | Dialogue and scene reasoning; short-context Standard rates. |
| gpt-6-luna | 0.10 | 0.01 | 0.50 | Optional cost-sensitive model; not selected in the examples. |
| gpt-5.6-sol | 4.00 | 0.40 | 20.00 | Optional GPT-5.6 flagship. Promotional rates available at least through November 21, 2026. |
| gpt-5.6-terra | 2.00 | 0.20 | 12.00 | Fast, utility, post-processing and sentiment profiles. |
| gpt-5.6-luna | 0.20 | 0.02 | 1.20 | Optional cost-sensitive model; not selected in the examples. |
| gpt-6-astra | 10.00 | 1.00 | 50.00 | Optional higher-capability model; not selected in the examples. |

The examples use GPT-5.6 Terra for short tasks instead of Luna, while dialogue and scene reasoning retain GPT-6.1 Sol. GPT-5.6 Sol and Luna are available as alternatives through the price table.

Model IDs and prices checked October 6, 2026 against [OpenAI's model catalog](https://developers.openai.com/api/docs/models/all) and [Standard API pricing](https://developers.openai.com/api/docs/pricing). These text models support the plain Chat Completions calls used here; function calling has additional API/reasoning restrictions.

The Gemini example uses gemini-3.1-flash-tts-preview (Preview), the newest model supported by this adapter's Cloud Text-to-Speech endpoint. Its input/output rates are $1/$20 per million text/audio tokens. Gemini 2.5 Flash remains a cheaper stable option at $0.50/$10. Gemini 3.8 requires the Gemini Enterprise API and cannot be substituted into this adapter. See [Cloud TTS models](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts) and [Google TTS pricing](https://cloud.google.com/text-to-speech/pricing).

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

GPT-5.6 references: [Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol), [Terra](https://developers.openai.com/api/docs/models/gpt-5.6-terra), [Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna).
