using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
public sealed class RedditPostSource
{
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
}

public enum PitchStatus
{
    Draft,
    PostedForVote,
    Approved,
    Rejected,
    Expired,
    QueuedAsIdea
}

[Serializable]
public sealed class PitchCandidate
{
    public string id;
    public RedditPostSource source;
    public string body;
    public string approvalReason;
    public int discordColor;
    public string channelKey;
    public string context;
    public PitchStatus status;
    public string discordMessageId;
    public string discordChannelId;
    public string discordChannelKey;
    public string createdAtUtc;
    public string expiresAtUtc;
    public int upVotes;
    public int downVotes;
    public int voteScore;
    public string queuedAtUtc;
    public string generatorSlug;
}

public sealed class PitchCandidateStore
{
    private static readonly object instanceLock = new object();
    private static readonly Dictionary<string, PitchCandidateStore> instances = new Dictionary<string, PitchCandidateStore>(StringComparer.OrdinalIgnoreCase);

    private readonly object sync = new object();
    private readonly ChatManagerContext context;
    private readonly ChatGenerator generator;
    private readonly string path;
    private int minimumVotesToQueue = 1;
    private int approvalScore = 1;
    private PitchCandidateManifest manifest = new PitchCandidateManifest();

    public PitchCandidateStore(ChatManagerContext context, ChatGenerator generator)
    {
        this.context = context;
        this.generator = generator;
        path = $"reddit-pitches-{context.Key}.json";
        manifest = Load();

        lock (instanceLock)
            instances[context.Key] = this;
    }

    public void ConfigureVoting(int minimumVotes, int approvalScore)
    {
        minimumVotesToQueue = Mathf.Max(0, minimumVotes);
        this.approvalScore = Mathf.Max(1, approvalScore);
    }

    public void Save(PitchCandidate candidate)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.id))
            return;

        StampContext(candidate);

        lock (sync)
        {
            var existing = manifest.candidates.FirstOrDefault(p => string.Equals(p.id, candidate.id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                manifest.candidates.Add(candidate);
            else
                manifest.candidates[manifest.candidates.IndexOf(existing)] = candidate;

            Write();
        }
    }

    public IReadOnlyList<DiscordMessageRef> GetPostedPitchMessages(string discordChannelId, string excludeMessageId = null)
    {
        if (string.IsNullOrWhiteSpace(discordChannelId))
            return Array.Empty<DiscordMessageRef>();

        lock (sync)
        {
            return manifest.candidates
                .Where(candidate => candidate != null)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.discordMessageId))
                .Where(candidate => string.Equals(candidate.discordChannelId, discordChannelId, StringComparison.OrdinalIgnoreCase))
                .Where(candidate => string.IsNullOrWhiteSpace(excludeMessageId) || !string.Equals(candidate.discordMessageId, excludeMessageId, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => new DiscordMessageRef
                {
                    channelId = candidate.discordChannelId,
                    messageId = candidate.discordMessageId
                })
                .GroupBy(item => item.messageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
    }

    public static IReadOnlyList<PitchCandidate> GetPitchStatus(string channelKey = null, int limit = 50)
    {
        var candidates = new Dictionary<string, PitchCandidate>(StringComparer.OrdinalIgnoreCase);

        lock (instanceLock)
        {
            foreach (var candidate in instances.Values.SelectMany(store => store.GetCandidates(null)))
                candidates[candidate.id] = candidate;
        }

        foreach (var candidate in LoadPersistedCandidates())
            if (!candidates.ContainsKey(candidate.id))
                candidates[candidate.id] = candidate;

        var query = candidates.Values
            .Where(candidate => candidate != null)
            .Where(candidate => candidate.status != PitchStatus.Rejected && candidate.status != PitchStatus.Expired);

        if (!string.IsNullOrWhiteSpace(channelKey))
            query = query.Where(candidate => string.Equals(candidate.channelKey, channelKey, StringComparison.OrdinalIgnoreCase));

        return query
            .OrderByDescending(candidate => candidate.createdAtUtc)
            .Take(Mathf.Clamp(limit, 1, 200))
            .ToList();
    }

    private IReadOnlyList<PitchCandidate> GetCandidates(string channelKey)
    {
        lock (sync)
        {
            var candidates = manifest.candidates
                .Where(candidate => candidate != null)
                .Where(candidate => candidate.status != PitchStatus.Rejected && candidate.status != PitchStatus.Expired)
                .Select(candidate =>
                {
                    StampContext(candidate);
                    return candidate;
                });

            if (!string.IsNullOrWhiteSpace(channelKey))
                candidates = candidates.Where(candidate => string.Equals(candidate.channelKey, channelKey, StringComparison.OrdinalIgnoreCase));

            return candidates.ToList();
        }
    }

    public static bool TryApplyVote(string messageId, string channelId, int upDelta, int downDelta, string source)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return false;

        lock (instanceLock)
        {
            foreach (var store in instances.Values)
                if (store.ApplyVoteByDiscordMessage(messageId, channelId, upDelta, downDelta, source))
                    return true;
        }

        return false;
    }

    public static PitchCandidate ApplyVote(string channelKey, string id, int upDelta, int downDelta, string source)
    {
        if (string.IsNullOrWhiteSpace(channelKey) || string.IsNullOrWhiteSpace(id))
            return null;
        if (upDelta == 0 && downDelta == 0)
            return null;

        lock (instanceLock)
        {
            if (instances.TryGetValue(channelKey, out var store))
                return store.ApplyVoteById(channelKey, id, upDelta, downDelta, source);

            foreach (var candidateStore in instances.Values)
            {
                var updated = candidateStore.ApplyVoteById(channelKey, id, upDelta, downDelta, source);
                if (updated != null)
                    return updated;
            }
        }

        return null;
    }

    public int ResolveFinishedVotes(int minimumVotes)
    {
        lock (sync)
        {
            var changed = false;
            var queued = 0;
            var voteMinimum = Mathf.Max(0, minimumVotes);

            foreach (var candidate in manifest.candidates.Where(candidate => candidate != null && candidate.status == PitchStatus.PostedForVote).ToList())
            {
                if (!IsExpired(candidate))
                    continue;

                ResolveFinishedCandidate(candidate, voteMinimum);
                DeletePollMessage(candidate);
                changed = true;
                if (candidate.status == PitchStatus.QueuedAsIdea)
                    queued++;
            }

            if (changed)
                Write();

            return queued;
        }
    }

    public void QueueCandidate(PitchCandidate candidate, string eventName = "pitch_queued")
    {
        if (candidate == null)
            return;

        StampContext(candidate);

        lock (sync)
        {
            var existing = manifest.candidates.FirstOrDefault(p => string.Equals(p.id, candidate.id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                manifest.candidates.Add(candidate);
            else
                manifest.candidates[manifest.candidates.IndexOf(existing)] = candidate;

            QueueApprovedCandidate(candidate, eventName);
            Write();
        }
    }

    private bool ApplyVoteByDiscordMessage(string messageId, string channelId, int upDelta, int downDelta, string source)
    {
        lock (sync)
        {
            var candidate = manifest.candidates.FirstOrDefault(p =>
                string.Equals(p.discordMessageId, messageId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(channelId) || string.Equals(p.discordChannelId, channelId, StringComparison.OrdinalIgnoreCase)));

            if (candidate == null)
                return false;

            var changed = ApplyVoteToCandidate(candidate, upDelta, downDelta, source);
            if (changed)
                Write();

            return true;
        }
    }

    private PitchCandidate ApplyVoteById(string channelKey, string id, int upDelta, int downDelta, string source)
    {
        lock (sync)
        {
            var candidate = manifest.candidates.FirstOrDefault(p =>
                string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(channelKey) || string.Equals(p.channelKey, channelKey, StringComparison.OrdinalIgnoreCase)));

            if (candidate == null)
                return null;

            if (ApplyVoteToCandidate(candidate, upDelta, downDelta, source))
                Write();

            StampContext(candidate);
            return candidate;
        }
    }

    private bool ApplyVoteToCandidate(PitchCandidate candidate, int upDelta, int downDelta, string source)
    {
        if (candidate == null)
            return false;

        if (candidate.status != PitchStatus.PostedForVote)
            return false;

        if (IsExpired(candidate))
            return false;

        candidate.upVotes = Mathf.Max(0, candidate.upVotes + upDelta);
        candidate.downVotes = Mathf.Max(0, candidate.downVotes + downDelta);
        candidate.voteScore = candidate.upVotes - candidate.downVotes;

        Debug.Log($"Pitch vote updated: {candidate.id} -> up {candidate.upVotes}, down {candidate.downVotes}, score {candidate.voteScore}");
        if (HasWinningVote(candidate))
            QueueApprovedCandidate(candidate, "pitch_vote_won");
        return true;
    }

    private void ResolveFinishedCandidate(PitchCandidate candidate, int minimumVotes)
    {
        if (candidate == null || candidate.status == PitchStatus.QueuedAsIdea)
            return;

        candidate.voteScore = Mathf.Max(0, candidate.upVotes) - Mathf.Max(0, candidate.downVotes);
        var totalVotes = Mathf.Max(0, candidate.upVotes) + Mathf.Max(0, candidate.downVotes);
        if (totalVotes < Mathf.Max(0, minimumVotes))
            candidate.status = PitchStatus.Expired;
        else if (candidate.voteScore >= approvalScore)
            QueueApprovedCandidate(candidate);
        else if (candidate.voteScore < 0)
            candidate.status = PitchStatus.Rejected;
        else
            candidate.status = PitchStatus.Expired;
    }

    private bool HasWinningVote(PitchCandidate candidate)
    {
        if (candidate == null || candidate.status != PitchStatus.PostedForVote)
            return false;

        var totalVotes = Mathf.Max(0, candidate.upVotes) + Mathf.Max(0, candidate.downVotes);
        return totalVotes >= minimumVotesToQueue && candidate.voteScore >= approvalScore;
    }

    private void QueueApprovedCandidate(PitchCandidate candidate, string eventName = "pitch_queued")
    {
        if (generator == null)
            return;

        candidate.status = PitchStatus.QueuedAsIdea;
        candidate.queuedAtUtc = DateTime.UtcNow.ToString("O");
        generator.AddIdeaToQueue(PitchToIdeaConverter.Convert(candidate));
        OperatorTelemetry.RecordEvent(eventName, $"Queued pitch: {PitchCandidateText.GetTitle(candidate)}", context, context.Key);
    }

    private void DeletePollMessage(PitchCandidate candidate)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.discordMessageId))
            return;

        var contextKey = context?.Key ?? candidate.channelKey;
        DiscordManager.DeleteWebhookMessageForContext(contextKey, candidate.discordChannelKey, candidate.discordMessageId);
    }

    private bool IsExpired(PitchCandidate candidate)
    {
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.expiresAtUtc))
            return false;
        return DateTimeOffset.TryParse(candidate.expiresAtUtc, out var expiresAt) && DateTimeOffset.UtcNow > expiresAt;
    }

    private void StampContext(PitchCandidate candidate)
    {
        if (candidate == null)
            return;
        if (string.IsNullOrWhiteSpace(candidate.channelKey))
            candidate.channelKey = context?.Key ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate.context))
            candidate.context = context?.Name ?? string.Empty;
    }

    private PitchCandidateManifest Load()
    {
        try
        {
            if (!File.Exists(path))
                return new PitchCandidateManifest();

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<PitchCandidateManifest>(json) ?? new PitchCandidateManifest();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PitchCandidateStore.Load failed for '{path}': {e.Message}");
            return new PitchCandidateManifest();
        }
    }

    private static IEnumerable<PitchCandidate> LoadPersistedCandidates()
    {
        IEnumerable<string> files;

        try
        {
            files = Directory.GetFiles(".", "reddit-pitches-*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PitchCandidateStore.LoadPersistedCandidates failed to list manifests: {e.Message}");
            yield break;
        }

        foreach (var file in files)
        {
            PitchCandidateManifest fileManifest;

            try
            {
                var json = File.ReadAllText(file);
                fileManifest = JsonConvert.DeserializeObject<PitchCandidateManifest>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"PitchCandidateStore.LoadPersistedCandidates failed for '{file}': {e.Message}");
                continue;
            }

            if (fileManifest?.candidates == null)
                continue;

            var fileChannelKey = Path.GetFileNameWithoutExtension(file)
                .Replace("reddit-pitches-", string.Empty);

            foreach (var candidate in fileManifest.candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.id))
                    continue;
                if (string.IsNullOrWhiteSpace(candidate.channelKey))
                    candidate.channelKey = fileChannelKey;
                if (string.IsNullOrWhiteSpace(candidate.context))
                    candidate.context = fileChannelKey;
                yield return candidate;
            }
        }
    }

    private void Write()
    {
        try
        {
            manifest.candidates = manifest.candidates
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.id))
                .GroupBy(p => p.id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(p => p.createdAtUtc)
                .Take(200)
                .ToList();

            File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"PitchCandidateStore.Write failed for '{path}': {e.Message}");
        }
    }

    [Serializable]
    private sealed class PitchCandidateManifest
    {
        public List<PitchCandidate> candidates = new List<PitchCandidate>();
    }
}

public static class PitchToIdeaConverter
{
    public static Idea Convert(PitchCandidate candidate)
    {
        var source = candidate.source;
        var body = new StringBuilder();

        body.AppendLine("APPROVED REDDIT PITCH");
        body.AppendLine();
        body.AppendLine("SOURCE:");
        body.AppendLine($"Title: {source?.title}");
        body.AppendLine($"Subreddit: {source?.subreddit}");
        body.AppendLine($"Author: {source?.author}");
        body.AppendLine($"Post ID: {source?.id}");
        body.AppendLine($"Karma: {source?.score ?? 0}");
        body.AppendLine($"Comments: {source?.commentCount ?? 0}");
        body.AppendLine($"Link: {BuildSourceUrl(source)}");
        body.AppendLine();
        body.AppendLine("PITCH CARD:");
        body.AppendLine(PitchCandidateText.GetBody(candidate));
        body.AppendLine();
        body.AppendLine("EVALUATOR APPROVAL:");
        body.AppendLine(string.IsNullOrWhiteSpace(candidate.approvalReason) ? "No evaluator approval reason recorded." : candidate.approvalReason);
        body.AppendLine();
        body.AppendLine("RAW REDDIT TEXT:");
        body.AppendLine(string.IsNullOrWhiteSpace(source?.selftext) ? "(No post body.)" : source.selftext);
        body.AppendLine();
        body.AppendLine("THREAD MATERIAL:");
        body.AppendLine(string.IsNullOrWhiteSpace(source?.threadContext) ? "(No mined thread context.)" : source.threadContext);
        body.AppendLine();
        body.AppendLine("GENERATION NOTES:");
        body.AppendLine("Use the approved pitch as the spin, not as the only source. Preserve useful details from the raw Reddit text and thread material, while letting continuity and the pitch frame shape the scene.");

        return new Idea(body.ToString(), source?.author ?? "reddit", source?.subreddit ?? "reddit", source?.id);
    }

    public static string BuildSourceUrl(RedditPostSource source)
    {
        if (source == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(source.permalink))
            return source.permalink.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? source.permalink : "https://reddit.com" + source.permalink;
        if (!string.IsNullOrWhiteSpace(source.url))
            return source.url;
        return string.Empty;
    }
}

public static class PitchDiscordPublisher
{
    public static void Publish(PitchCandidate candidate, PitchCandidateStore store)
    {
        Publish(candidate, store, null);
    }

    public static void Publish(PitchCandidate candidate, PitchCandidateStore store, ChatManagerContext context)
    {
        if (candidate == null || store == null)
            return;

        var embed = new DiscordEmbed
        {
            Title = Trim(PitchCandidateText.GetTitle(candidate), 250),
            Description = Trim(BuildDescription(candidate), 4000),
            Color = candidate.discordColor != 0 ? candidate.discordColor : 0x4CA3FF
        };

        candidate.status = PitchStatus.PostedForVote;
        var contextKey = context?.Key ?? candidate.channelKey;
        DiscordManager.PutInQueueForContext(contextKey, candidate.discordChannelKey, new DiscordWebhookMessage("# :new: From Reddit!", null, null, embed), posted =>
        {
            if (posted != null)
            {
                var previousPitchMessages = store.GetPostedPitchMessages(posted.channel_id, posted.id);
                candidate.discordMessageId = posted.id;
                candidate.discordChannelId = posted.channel_id;
                store.Save(candidate);
                DiscordBotService.Instance?.PinPitchMessage(posted, previousPitchMessages);
                DiscordBotService.Instance?.AddDefaultPitchReactions(posted);
            }
        });

        store.Save(candidate);
    }

    private static string BuildDescription(PitchCandidate candidate)
    {
        var pitch = PitchCandidateText.GetPitch(candidate);
        var cast = PitchCandidateText.GetCast(candidate);
        var reason = string.IsNullOrWhiteSpace(candidate.approvalReason) ? "No evaluator approval reason recorded." : candidate.approvalReason;
        var source = candidate.source;
        var sourceUrl = PitchToIdeaConverter.BuildSourceUrl(source);
        var sourceTitle = string.IsNullOrWhiteSpace(source?.title) ? "Untitled Reddit source" : source.title;
        var sourceLabel = string.IsNullOrWhiteSpace(sourceUrl)
            ? sourceTitle
            : $"[{sourceTitle}]({sourceUrl})";
        var lines = new List<string>();

        lines.Add($"**Source**\n{sourceLabel}\n{source?.subreddit ?? "reddit"} | {FormatCount(source?.score ?? 0, "karma")} | {FormatCount(source?.commentCount ?? 0, "comments")}");
        if (!string.IsNullOrWhiteSpace(pitch))
            lines.Add($"**Pitch**\n{pitch}");
        if (!string.IsNullOrWhiteSpace(cast))
            lines.Add($"**Cast**\n{cast}");

        lines.Add($"**Why it passed**\n{reason}");

        return string.Join("\n\n", lines);
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
    }

    private static string FormatCount(int value, string label)
    {
        return $"{Mathf.Max(0, value):N0} {label}";
    }
}

public sealed class DiscordMessageRef
{
    public string channelId;
    public string messageId;
}

public static class PitchCandidateFactory
{
    public static PitchCandidate FromText(string text, RedditPostSource source, string generatorSlug, string discordChannelKey, int expirationMinutes, string approvalReason = null)
    {
        var now = DateTime.UtcNow;
        return new PitchCandidate
        {
            id = source?.id ?? Guid.NewGuid().ToString("N"),
            source = source,
            body = NormalizeBody(text, source),
            approvalReason = approvalReason,
            status = PitchStatus.Draft,
            discordChannelKey = discordChannelKey,
            createdAtUtc = now.ToString("O"),
            expiresAtUtc = now.AddMinutes(Mathf.Max(1, expirationMinutes)).ToString("O"),
            generatorSlug = generatorSlug
        };
    }

    private static string NormalizeBody(string text, RedditPostSource source)
    {
        if (!string.IsNullOrWhiteSpace(text))
            return text.Trim();

        return $"Title: {source?.title ?? "Untitled Reddit pitch"}\nPitch: No pitch body generated.\nCast: None specified.";
    }
}

public static class PitchCandidateText
{
    public static string GetBody(PitchCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate?.body))
            return candidate.body.Trim();

        return $"Title: {candidate?.source?.title ?? "Untitled Reddit pitch"}\nPitch: No pitch body generated.\nCast: None specified.";
    }

    public static string GetTitle(PitchCandidate candidate)
    {
        var sections = GetBody(candidate).Parse("Title", "Pitch Title");
        foreach (var value in sections.Values)
            if (!string.IsNullOrWhiteSpace(value))
                return FirstLine(value);

        if (!string.IsNullOrWhiteSpace(candidate?.source?.title))
            return candidate.source.title.Trim();

        return FirstLine(GetBody(candidate));
    }

    public static string GetPitch(PitchCandidate candidate)
    {
        var body = GetBody(candidate);
        var value = body.Find("Pitch");
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        var sections = body.Parse("Pitch");
        return sections.TryGetValue("Pitch", out value) ? value : string.Empty;
    }

    public static string GetCast(PitchCandidate candidate)
    {
        var body = GetBody(candidate);
        var value = body.Find("Cast");
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        var sections = body.Parse("Cast");
        return sections.TryGetValue("Cast", out value) ? value : string.Empty;
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Untitled Reddit pitch";

        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('-', '*').Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "Untitled Reddit pitch";
    }
}

public static class PitchCandidateEvaluator
{
    public static bool IsApproved(string evaluation)
    {
        var verdict = evaluation.Find("Verdict");
        if (string.IsNullOrWhiteSpace(verdict))
            verdict = evaluation.Find("Decision");

        return verdict.IndexOf("approve", StringComparison.OrdinalIgnoreCase) >= 0 ||
            verdict.IndexOf("pass", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string GetReason(string evaluation)
    {
        var reason = evaluation.Find("Reason");
        if (!string.IsNullOrWhiteSpace(reason))
            return reason;

        var issue = evaluation.Find("Issue");
        if (!string.IsNullOrWhiteSpace(issue))
            return issue;

        return string.IsNullOrWhiteSpace(evaluation) ? "No evaluator reason returned." : PitchCandidateTextPreview(evaluation, 180);
    }

    private static string PitchCandidateTextPreview(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max - 3) + "...";
    }
}
