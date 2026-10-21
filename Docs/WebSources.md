# Web sources and voting

`RedditSource` and `PolymarketSource` implement `IWebSource` through `WebSource`. Adapters discover documents and remember accepted source IDs; `WebSource` owns cadence, batch limits, pitch generation/evaluation, voting delivery and direct Ideas. Each adapter has independent settings and runs alongside the other.

Existing scenes register the optional Polymarket adapter from `ChatManagerContext.Start`, using the Reddit generator when available. It remains idle unless a `polymarket` configuration block exists. No scene edits, trading account, API key, or wallet are needed. The left-arrow manual drop runs all configured web sources for the active show; overlapping drops from the same adapter are ignored.

## Configuration

Add the following block to the show's JSON configuration array. Public data comes from Gamma; requests only read market data.

```json
{
  "Type": "polymarket",
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
  "EnablePitchGate": true,
  "PitchDiscordChannel": "#stream",
  "PitchExpirationMinutes": 180,
  "PitchApprovalScore": 2,
  "PitchMinimumVotesToQueue": 1,
  "PitchAutoApprovalBatchSize": 0
}
```

Both sources accept the same `Batch` block and `Pitch*` fields. The batch policy is separate from provider-specific subreddit/mining or market-discovery configuration. Flat batch fields remain supported for existing Reddit configurations. If `Batch` is supplied, it replaces the flat batch policy in full; omitted nested fields use shared defaults (20/20/1, 60 minutes, offset 00:00). Removing the source block disables it on the next startup; use `Enabled: false` when reloading configuration at runtime.

- `BatchSize`: documents requested per adapter pass, and maximum passes per iteration, preserving Reddit's existing traversal budget. Reddit advances to another configured subreddit each pass; Polymarket rotates configured topic tags and scans up to `DiscoveryPages` for each pass.
- `BatchIterations`: number of rounds of those passes. Within one drop, each source document is evaluated at most once, even if repeated in discovery results.
- `BatchSizeLimit`: strict total accepted pitches/direct Ideas per drop, including autoapproved pitches. Evaluator rejections do not consume this limit. Zero disables intake; expired votes are still resolved at scheduled drops.
- `BatchPeriodOffset` and `BatchPeriodInMinutes`: wall-clock schedule aligned from midnight plus the offset, in local time. Period must be between one second and one year. Reloading replaces the schedule; stale asynchronous work cannot post or queue after reload/destruction.
- `ActiveHoursStart` / `ActiveHoursEnd`: allow automatic approvals during that window. Overnight windows are supported; equal times mean all day. Discovery and viewer voting continue outside it.
- `PitchAutoApprovalBatchSize: 0`: every evaluator-approved pitch goes to viewers. Positive values allow that many approvals to bypass voting per active drop.
- Voting thresholds are stored on each new pitch. Different sources may use different rules, including across restarts. Legacy Reddit pitches use Reddit's configured thresholds.
- `LogoUrl` and `BrandColor`: optional logo URL and integer Discord color overrides. Defaults use Reddit orange (`16729344`, `#FF4500`) and Polymarket blue (`3038463`, `#2E5CFF`). Embeds show the provider name and logo in the author line and thumbnail. Brand color replaces the former cast-based pitch color.

## Polymarket selection and context

Discovery requests open, active, unarchived events ordered by 24-hour volume, optionally filtered by tag slug. `TagSlugs: []` includes all topics. Each event supplies at most one candidate: the most active eligible open market, meeting **market-level** liquidity and 24-hour volume minima. Missing or malformed outcome prices are skipped. JSON-encoded and native outcome arrays are supported. Pagination, request spacing, 15-second request timeouts, and bounded retries for rate limits/transient server failures limit discovery work.

Accepted event IDs are saved immediately in `polymarket-{show}.json` and suppressed until `RepeatCooldownHours` passes. After the cooldown, a fresh snapshot gets a distinct pitch ID and voting window. This version does not calculate price movements. A reported market end date is retained as metadata; it is not assumed to prove the event has happened or the market has resolved.

Voting embeds display the selected outcome prices, market liquidity, 24-hour volume and snapshot time. Approved Ideas retain these data, the source link, description/resolution rules, resolution source, event/market IDs and uncertainty instructions. Prices describe market-implied expectations; they are not factual verification, representative opinion polling, or explanations for why an event occurred.

## Prompts and compatibility

Existing `Vault/{show}/Prompts/Reddit Source` prompts remain in use. Polymarket loads its pitch, evaluator and direct-generation prompts from Markdown files in the show’s Obsidian Vault:

```text
Vault/{show}/Prompts/Polymarket Source.md
Vault/{show}/Prompts/Polymarket Source/Pitch Candidate.md
Vault/{show}/Prompts/Polymarket Source/Pitch Evaluator.md
```

These files are installed alongside the Reddit prompts in the polbots, AppyDays, RomeBots and SpaceDrivel vault folders. Edit them directly in Obsidian; the runtime reads them for each generation. A missing prompt is reported as a batch failure rather than replaced by an embedded prompt. Vault content is locally managed and ignored by Git, so include these files when copying the setup to another machine.

Polymarket pitch arguments follow the same order as Reddit: source JSON `{0}`, supporting material `{1}`, available cast `{2}`, memory `{3}`, UTC time `{4}`, episode continuity `{5}`. Evaluator arguments use pitch text `{5}` and continuity `{6}`. The direct-generation template uses source context `{0}` and UTC time `{1}`. Substitution is single-pass so braces in supplied material stay literal.

Both sources share one pitch store per show. The on-disk filename intentionally remains `reddit-pitches-{show}.json`, preserving existing votes, Discord bindings, statuses and manifests without a migration. New documents add a `provider` field; missing provider metadata means Reddit. Existing pitch IDs remain unchanged; new IDs include the provider to prevent cross-source collisions. New Polymarket pitch IDs also identify the individual voting window. Source provenance remains available in pitch-status JSON and generated Ideas.

Run regression checks with `./Tests/WebSources/Run.ps1`. This compiles the production adapters, batching pipeline, pitch service/store, parser, configs, and Idea conversion against external boundary stubs. It does not send Discord messages or call an LLM. Also run `./Tests/Discord/Run.ps1` and `./Tests/Context/Run.ps1` after changing shared voting or prompt resolution.

API references: [event discovery](https://docs.polymarket.com/api-reference/events/list-events), [price interpretation](https://help.polymarket.com/en/articles/13364488-how-are-prices-calculated).
