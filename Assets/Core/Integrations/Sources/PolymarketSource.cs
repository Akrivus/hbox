using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class PolymarketSource : WebSource, IConfigurable<PolymarketConfigs>
{
    public override string SourceKey => "polymarket";
    public override string PromptRoot => "Polymarket Source";
    private PolymarketConfigs settings;
    private Dictionary<string, DateTime> history = new Dictionary<string, DateTime>();
    private string historyPath;
    private DateTime nextRequestUtc;
    private int tagIndex;

    protected override void RegisterConfig()
    {
        chatManagerContext.ConfigManager.RegisterConfig(typeof(PolymarketConfigs), SourceKey, value => Configure((PolymarketConfigs)value));
    }

    public void Configure(PolymarketConfigs config)
    {
        if (config.DiscoveryPageSize < 1 || config.DiscoveryPageSize > 100 || config.DiscoveryPages < 1 || config.DiscoveryPages > 20 ||
            !FiniteNonnegative(config.MinLiquidity) || !FiniteNonnegative(config.MinVolume24hr) ||
            !FiniteNonnegative(config.RepeatCooldownHours) || !FiniteNonnegative(config.RequestSpacingSeconds) ||
            config.MaxRequestAttempts < 1 || config.MaxRequestAttempts > 5)
            throw new ArgumentException("Invalid Polymarket discovery, liquidity, cooldown, or request settings.");
        settings = config;
        historyPath = $"polymarket-{chatManagerContext.Key}.json";
        try
        {
            history = File.Exists(historyPath)
                ? JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(historyPath)) ?? new Dictionary<string, DateTime>()
                : new Dictionary<string, DateTime>();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Cannot load Polymarket history: {e.Message}");
            history = new Dictionary<string, DateTime>();
        }
        ConfigureWebSource(config);
    }

    private static bool FiniteNonnegative(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    public override async Task<IReadOnlyList<WebSourceDocument>> FetchBatchAsync(int limit)
    {
        if (settings == null || limit <= 0) return Array.Empty<WebSourceDocument>();
        var config = settings;
        var tags = (config.TagSlugs ?? new List<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList();
        var tag = tags.Count == 0 ? null : tags[tagIndex++ % tags.Count];
        var results = new List<WebSourceDocument>();
        for (var page = 0; page < config.DiscoveryPages && results.Count < limit; page++)
        {
            var url = BuildEventsUrl(config, tag, page);
            var events = JArray.Parse(await DownloadAsync(url, config));
            foreach (var item in events.OfType<JObject>())
            {
                var id = item.Value<string>("id");
                if (string.IsNullOrWhiteSpace(id) || history.TryGetValue(id, out var acceptedAt) &&
                    DateTime.UtcNow - acceptedAt < TimeSpan.FromHours(config.RepeatCooldownHours)) continue;
                var source = ParseEvent(item, config, DateTime.UtcNow);
                if (source != null && results.All(existing => existing.id != source.id)) results.Add(source);
                if (results.Count >= limit) break;
            }
            if (events.Count < config.DiscoveryPageSize) break;
        }
        return results;
    }

    public static string BuildEventsUrl(PolymarketConfigs config, string tag, int page) =>
        "https://gamma-api.polymarket.com/events?active=true&closed=false&archived=false&order=volume24hr&ascending=false" +
        $"&limit={config.DiscoveryPageSize}&offset={page * config.DiscoveryPageSize}" +
        (string.IsNullOrWhiteSpace(tag) ? "" : "&tag_slug=" + Uri.EscapeDataString(tag));

    private async Task<string> DownloadAsync(string url, PolymarketConfigs config)
    {
        for (var attempt = 0; ; attempt++)
        {
            var wait = nextRequestUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            nextRequestUtc = DateTime.UtcNow.AddSeconds(config.RequestSpacingSeconds);
            try
            {
                // TimeoutWebClient bounds the underlying request as well as retries.
                return await Task.Run(() =>
                {
                    using var client = new TimeoutWebClient();
                    client.Headers[HttpRequestHeader.UserAgent] = "HBOx-PolymarketSource/1.0";
                    return client.DownloadString(url);
                });
            }
            catch (WebException e) when (attempt + 1 < config.MaxRequestAttempts && IsTransient(e))
            {
                var response = e.Response as HttpWebResponse;
                var delay = Math.Min(60, Math.Pow(2, attempt + 1));
                if (double.TryParse(response?.Headers["Retry-After"], NumberStyles.Float, CultureInfo.InvariantCulture, out var retryAfter))
                    delay = Math.Min(300, Math.Max(delay, retryAfter));
                response?.Dispose();
                nextRequestUtc = DateTime.UtcNow.AddSeconds(delay);
            }
        }
    }

    private static bool IsTransient(WebException error)
    {
        var status = (int?)((error.Response as HttpWebResponse)?.StatusCode);
        return error.Status == WebExceptionStatus.Timeout || error.Status == WebExceptionStatus.ConnectFailure ||
            status == 429 || status >= 500;
    }

    // One candidate per event; choose the most active eligible market to avoid near-duplicate outcome pitches.
    public static WebSourceDocument ParseEvent(JObject item, PolymarketConfigs config, DateTime snapshotUtc)
    {
        if (item.Value<bool?>("closed") == true || item.Value<bool?>("archived") == true || item.Value<bool?>("active") == false)
            return null;
        var eventId = item.Value<string>("id");
        var slug = item.Value<string>("slug");
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(slug)) return null;
        var markets = (item["markets"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(m => m.Value<bool?>("closed") != true && m.Value<bool?>("archived") != true && m.Value<bool?>("active") != false)
            .Where(m => Number(m, "liquidityNum", "liquidity") >= config.MinLiquidity && Number(m, "volume24hr") >= config.MinVolume24hr)
            .OrderByDescending(m => Number(m, "volume24hr"));
        foreach (var market in markets)
        {
            var names = ArrayValue(market["outcomes"]);
            var rawPrices = ArrayValue(market["outcomePrices"]);
            if (names == null || names.Length < 2 || rawPrices == null || names.Length != rawPrices.Length) continue;
            var prices = new List<double>();
            foreach (var value in rawPrices)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) ||
                    !FiniteNonnegative(price) || price > 1) break;
                prices.Add(price);
            }
            if (prices.Count != names.Length) continue;
            var question = market.Value<string>("question");
            if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(market.Value<string>("id"))) continue;
            var snapshot = new PolymarketSnapshot
            {
                eventId = eventId,
                eventTitle = item.Value<string>("title"),
                marketId = market.Value<string>("id"),
                endDate = market.Value<string>("endDate"),
                resolutionSource = market.Value<string>("resolutionSource"),
                outcomes = names,
                prices = prices.ToArray(),
                volume24hr = Number(market, "volume24hr"),
                liquidity = Number(market, "liquidityNum", "liquidity")
            };
            return new WebSourceDocument
            {
                provider = "polymarket",
                id = eventId,
                title = question,
                author = "Polymarket",
                url = "https://polymarket.com/event/" + Uri.EscapeDataString(slug),
                selftext = market.Value<string>("description") ?? item.Value<string>("description") ?? string.Empty,
                threadContext = $"Event: {snapshot.eventTitle}\nMarket ID: {snapshot.marketId}\nReported end date: {snapshot.endDate}\nResolution source: {snapshot.resolutionSource}",
                fetchedAtUtc = snapshotUtc.ToUniversalTime().ToString("O"),
                market = snapshot
            };
        }
        return null;
    }

    private static string[] ArrayValue(JToken token)
    {
        try
        {
            var array = token is JArray list ? list : token?.Type == JTokenType.String ? JArray.Parse(token.Value<string>()) : null;
            return array?.Values<string>().ToArray();
        }
        catch (JsonException) { return null; }
    }

    private static double Number(JObject item, params string[] keys)
    {
        foreach (var key in keys)
            if (double.TryParse(item[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && FiniteNonnegative(value)) return value;
        return 0;
    }

    public override void RememberAccepted(WebSourceDocument source)
    {
        history[source.id] = DateTime.UtcNow;
        File.WriteAllText(historyPath, JsonConvert.SerializeObject(history, Formatting.Indented));
    }
}
