# Google Gemini TTS

Existing casts keep their current providers by default. Google voices such as
`en-US-Standard-D` retain their Standard voice settings and API-key authentication.
OpenAI voices remain on OpenAI until an actor is assigned a Gemini voice. Gemini uses
`gemini-3.1-flash-tts-preview` (Preview) and refreshable Google OAuth credentials.

## Local setup

1. Enable Cloud Text-to-Speech and billing in your Google Cloud project.
2. Give the signed-in account `aiplatform.endpoints.predict` (for example through
   `roles/aiplatform.user`), permission to synthesize speech, and permission to use
   the project's quota/billing (`serviceusage.services.use`).
3. Install the Google Cloud CLI if needed, then run:

   ```powershell
   gcloud auth application-default login
   gcloud auth application-default set-quota-project YOUR_PROJECT_ID
   ```

4. Alternatively, place a service-account key in `google-credentials.json` in
   the project root. The adapter checks `GoogleCredentialsPath`, then
   `GOOGLE_APPLICATION_CREDENTIALS`, then this root file, then
   `%APPDATA%/gcloud/application_default_credentials.json`. Explicit paths take
   precedence and are not silently replaced if invalid. It supports both
   `authorized_user` refresh tokens and `service_account` keys, obtaining and
   caching OAuth access tokens. Give the service account the permissions above.
   Workload federation and metadata-server ADC are not supported.
   Legacy Google voices also use OAuth when no Google API key is configured.

Optional settings in the existing `tts` config:

```json
{
  "Type": "tts",
  "GoogleApiKey": "YOUR_EXISTING_GOOGLE_TTS_API_KEY",
  "GoogleProjectId": "YOUR_PROJECT_ID",
  "GoogleGeminiModel": "gemini-3.1-flash-tts-preview",
  "MaxConcurrentRequests": 1
}
```

Routing uses the existing Actor Voice string. There is no provider dropdown.
The old global replacement switch and voice replacement dictionary are removed;
old JSON settings with those fields are ignored.

Updating service-account credentials does not select Gemini or upgrade Standard
voices. With a Google API key configured, Standard requests continue to use it.
GoogleProjectId overrides the quota project in the ADC file. Credentials do not
belong in source control.

## Cast and direction

Set an Actor's Voice to gemini:Kore (or gemini:Charon, etc.) to opt that actor
into Gemini. The prefix is removed before calling Google. Existing OpenAI voice
names such as coral still use OpenAI; existing Google names such as
en-US-Standard-D still use Standard Cloud TTS. No cast assets were migrated.

The following are provisional audition suggestions, not automatic mappings:

| Existing voice | Google voice |
| --- | --- |
| alloy | Kore |
| ash | Orus |
| ballad | Charon |
| coral | Aoede |
| echo | Puck |
| fable | Fenrir |
| onyx | Algenib |
| nova | Leda |
| sage | Sulafat |
| shimmer | Zephyr |
| verse | Algieba |

These are starting points for auditioning, not equivalent voices.

Actors also have `VoiceModel`, `VoiceLanguage` (default `en-US`), and
`VoiceInstructions`. `VoiceModel` defaults to the configured Gemini model.
`ChatNode.VoiceInstructions` can supply additional direction for a particular
line; it is appended to the actor's instructions. This field is optional and
does not change dialogue generation templates automatically.

For example: `Formal Roman senator, restrained irritation, dry comic timing.`
Keep directions separate from spoken text. Existing Unity pitch modulation
continues to apply during playback.

## Audio, limits and usage

Gemini requests mono LINEAR16 at 24 kHz. Legacy requests use LINEAR16 at
48 kHz; WAV headers are removed and sample format validated before playback.
Long text is split into chunks of at most 4,000 UTF-8 bytes, retaining every
character and avoiding split surrogate pairs. The same direction is sent with
each chunk; voice delivery may vary at chunk boundaries. Direction longer than
4,000 bytes fails clearly instead of being silently truncated.

Only network errors, timeouts, HTTP 429 and HTTP 5xx are retried, up to four times
per chunk. Invalid credentials and request errors fail immediately. Successful
chunks are not resubmitted when a later chunk has a transient failure.

Gemini usage records are estimates: input characters/4 (including directions for
each chunk) and audio duration × 25 output tokens/second. Add Gemini token prices
to your `openai` config's existing `ModelPrices` table so the shared usage dashboard
can price these records. The example config includes Flash and Pro prices.
Retries and failed partial utterances can incur additional costs that are not
included in the successful utterance estimate; Google billing is authoritative.

Validate with a short cast audition before generating full episodes: neutral
speech, sarcasm, whispers, shouting, Roman names and science-fiction names.
The opt-in `--live-root-credentials` test probe synthesizes only "Audio check."
using the root credentials file. Ordinary regression checks use mock HTTP.

Run the mock regression checks from the repository root with
`powershell -File Tests/TTS/Run.ps1` (requires .NET 10).

References: [Gemini TTS](https://docs.cloud.google.com/text-to-speech/docs/gemini-tts),
[pricing](https://cloud.google.com/text-to-speech/pricing).

Direct callers can use GetClipFromGemini or GetAudioDataFromGemini. Existing Google and OpenAI helpers always retain their named providers.

The example selects Gemini 3.1 Flash TTS Preview, the newest model supported by
this Cloud Text-to-Speech adapter as of October 6, 2026. Its price is $1 per
million input text tokens and $20 per million output audio tokens. For cheaper,
stable operation, use gemini-2.5-flash-tts ($0.50/$10). Gemini 3.8 is available
through a different API and is not compatible with this adapter.

This update changes examples only. The runtime default remains Gemini 2.5 Flash;
existing local configuration and cast voice assignments are unchanged.
