# Actor voice auditions

Open **HBOX > Actor Voice Auditions**. This channel-agnostic Editor tool uses any
existing `Actor` asset; it does not enumerate casts, schedule broadcasts, generate
dialogue, or finalize casting. No existing actor or scene asset is modified. Auditions do not replace global TTS
configuration or require a current ChatManager context.

## Audition and compare

1. Select an existing actor asset in A. Choose another actor in B, or select the
   same actor twice and enable **Temporary voice settings** in B. These settings
   are window-local, disposable comparisons, not writes to cast assets.
2. Keep the short standard phrase for comparable auditions, or enter a phrase
   of at most 500 UTF-8 bytes. Both slots use the same phrase. Instructions stay
   separate from the spoken phrase. All text uses the production Google scrubber.
3. Set **Local config file** to an existing JSON config containing a `tts` section
   (normally `hbox.json`) and click **Load TTS configuration (no request)**. Only
   that section is read, using the existing config loader; credentials are not
   displayed or saved in the window. Select the file containing the effective
   TTS settings you want to audition: this tool does not merge channel overrides.
   Load again after editing the file. No scene, Play Mode, or broadcast is needed.
4. Click **Generate A/B — paid request…**, inspect voice/model/language/text and
   instructions in the confirmation, then explicitly choose **Synthesize once**.
   One slot generates per confirmation. Nothing generates on selection, opening,
   editing, playback, or a cache miss. Automatic provider retries are disabled for
   auditions; a failure needs a fresh explicit confirmation. Normal episode retry
   behavior is unchanged. The provider or SDK may apply its own transport behavior;
   this tool does not promise an exact price or a billing refund on failure.
5. Click **Play cached A/B (free)** repeatedly to compare. Cached samples survive
   reopening the window and Editor restarts. Playback uses Unity's Editor audio
   preview API at neutral pitch, without scene sentiment/pitch modulation. Stop or
   close the window to stop playback. Stopping uses Unity's shared preview stop
   command and can also stop an Inspector audio preview.

Synthesis reuses production routing, request throttling, metering, Google
normalization, OAuth, decoding and actor instructions through
`TextToSpeechGenerator.GenerateAudition(ChatNode, TTSConfigs)`. The original
`GenerateTextToSpeech` episode entry point is unchanged. Gemini supports actor
language/model/instructions; other providers retain their production behavior
(including ignored direction controls). Actor speaking rate/pitch/volume fields
are not new provider controls in this tool. The preview is the raw synthesized
voice, not a claim of the final mix heard in an episode.

The cache lives in ignored `Library/ActorVoiceAuditions`. SHA-256 identity includes
schema version, actor asset GUID, exact phrase, selected voice, resolved provider
model, normalized default language, and actor voice instructions. Changing a
relevant setting cannot play the old sample under the new identity. Cache files
contain only sample ID, base64 mono PCM16 and sample rate, not credentials. Invalid
or missing cache files are misses; they never trigger synthesis. Delete a sample's
`<full SHA-256>.json` (or the cache folder) while idle to explicitly audition it
again. Future request/playback changes should bump the schema when needed.

## Deterministic checks

From repository root, with .NET 10 and Unity's resolved Newtonsoft package:

```powershell
powershell -File Tests/Auditions/Run.ps1
powershell -File Tests/TTS/Run.ps1
```

The audition harness compiles the real Actor, snapshot/cache and Google adapter
with minimal Unity type stubs. It covers missing actor/asset identity, blank voice,
phrase byte limits, immutable snapshots, preservation of originals, model fallback
and overrides, all request identity inputs, provider routing, durable cache hits,
corruption, explicit consent, empty/malformed/wrong-identity responses, concurrent
requests, failure recovery and single-attempt Google HTTP/network failures.
It also compiles the real TextToSpeechGenerator with compile-only collaborators
and exercises its audition entry point through mocked HTTP, verifying actor
configuration forwarding and independence from the current channel/global config.
It makes no paid requests and needs no real credentials.

## Remaining Unity Editor checks

- Compile the project in its supported Unity version. Open the window, drag actor
  assets from multiple channels, and verify non-assets cannot be used. Ensure no
  calls occur when opening/selecting/editing/canceling or when all 60 PBNN actors
  exist in the project.
- Load the chosen local config with Google OAuth credentials and enabled
  billing/permissions from `Docs/GoogleGeminiTTS.md`. Confirm one paid short Gemini
  audition with a known actor without entering Play Mode. Verify
  voice/model/language and actor direction; compare two actors and a temporary
  setting override on one actor. Verify unrelated global TTS settings are unchanged.
- Confirm cached audio is audible both in and out of Play Mode, at the expected
  sample rate, with repeated A/B playback making no network calls. Verify Stop,
  window close and Play Mode transitions stop preview audio without leaked clips.
  Unity's internal `AudioUtil.PlayPreviewClip` API must exist; unsupported versions
  show a warning instead of attempting synthesis as a fallback.
- Try missing/invalid credentials and an invalid model/voice. Confirm a visible
  failure, no cached failure, and another confirmation before retrying. Disable
  credentials after caching and verify listening still works.
- Change phrase/instructions/language/model and confirm a cache miss. Restore the
  original settings and confirm a hit. Inspect original actor assets and git diff
  after A/B work to confirm casting and scene definitions remain unchanged.

No live credentials, paid synthesis or audible playback are exercised by the
standalone harness. Passing it is not evidence that a particular voice sounds good.
