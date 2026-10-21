using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

public interface IWebSource
{
    string SourceKey { get; }
    string PromptRoot { get; }
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<WebSourceDocument>> FetchBatchAsync(int limit);
    void RememberAccepted(WebSourceDocument source);
}

[Serializable]
public class WebSourceDocument
{
    // Keep the legacy field names so saved Reddit pitches remain readable.
    public string provider = "reddit";
    public string id;
    public string title;
    public string selftext;
    public string author;
    public string subreddit;
    public string permalink;
    public string url;
    public int score;
    public int commentCount;
    public string threadContext;
    public string promptContext;
    public string fetchedAtUtc;
    public string logoUrl;
    public int? brandColor;
    public PolymarketSnapshot market;

    [JsonIgnore] public string Provider => string.IsNullOrWhiteSpace(provider) ? "reddit" : provider;
    [JsonIgnore] public string DisplayName => SourceBrand.Name(Provider);
    [JsonIgnore] public string IdeaSource => Provider == "reddit" ? subreddit ?? "reddit" : Provider;
    [JsonIgnore] public string Identity => Provider + ":" + id;
    [JsonIgnore] public string SourceUrl => !string.IsNullOrWhiteSpace(permalink)
        ? (permalink.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? permalink : "https://reddit.com" + permalink)
        : url ?? string.Empty;

    public string Summary()
    {
        if (market == null)
            return $"{subreddit ?? DisplayName} | {Math.Max(0, score):N0} karma | {Math.Max(0, commentCount):N0} comments";
        var outcomes = string.Join(" | ", (market.outcomes ?? Array.Empty<string>()).Select((name, index) =>
            index < (market.prices?.Length ?? 0) ? $"{name}: {market.prices[index].ToString("P1", CultureInfo.InvariantCulture)}" : name));
        return $"{outcomes}\n24h volume: ${market.volume24hr.ToString("N0", CultureInfo.InvariantCulture)} | Liquidity: ${market.liquidity.ToString("N0", CultureInfo.InvariantCulture)}\nSnapshot: {fetchedAtUtc}";
    }

    public string GenerationNotes => market == null
        ? "Use the approved pitch as the spin. Preserve useful source details and thread material while letting continuity shape the scene."
        : "Prices are timestamped market-implied expectations, not facts, polls, or endorsements. Respect the exact resolution rules. Do not invent news, reasons for price movements, or changes absent from this snapshot.";
}

[Serializable]
public sealed class PolymarketSnapshot
{
    public string eventId;
    public string eventTitle;
    public string marketId;
    public string endDate;
    public string resolutionSource;
    public string[] outcomes;
    public double[] prices;
    public double volume24hr;
    public double liquidity;
}

public static class SourceBrand
{
    public static string Name(string key) => key == "polymarket" ? "Polymarket" : key == "reddit" ? "Reddit" : key;
    public static int Color(string key) => key == "reddit" ? 0xFF4500 : 0x2E5CFF;
    public static string Logo(string key) => key == "reddit"
        ? "https://www.redditstatic.com/desktop2x/img/favicon/android-icon-192x192.png"
        : "https://polymarket.com/icons/android-chrome-192x192.png";
}
