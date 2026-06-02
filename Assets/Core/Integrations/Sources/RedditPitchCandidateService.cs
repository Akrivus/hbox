using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class RedditPitchCandidateService
{
    private const int ContinuityTelemetryScanLimit = 100;
    private const long MaxContinuityOutputBytes = 128 * 1024;

    private readonly ChatManagerContext context;
    private readonly ChatGenerator generator;

    public int MemoryMaxChars { get; set; } = 4000;
    public int ContinuityEpisodeLimit { get; set; } = 6;
    public int ContinuityMaxChars { get; set; } = 4000;
    public int SourceSelftextMaxChars { get; set; } = 1200;
    public int EvaluatorThreadMaxChars { get; set; } = 1800;
    public int EvaluatorMemoryMaxChars { get; set; } = 1200;
    public int EvaluatorContinuityMaxChars { get; set; } = 1200;
    public int EvaluatorActorLimit { get; set; } = 40;

    public RedditPitchCandidateService(ChatManagerContext context, ChatGenerator generator)
    {
        this.context = context;
        this.generator = generator;
    }

    public async Task<PitchCandidate> GenerateAsync(
        RedditPostSource source,
        string topic,
        string discordChannelKey,
        int expirationMinutes)
    {
        var actors = "- " + string.Join("\n- ", context.Actors.Select(a => a.Name));
        var chat = new Chat(new Idea(topic), generator.ManagerContext);
        var memory = LimitText(await MemoryBucket.GetContext(context, generator.slug, chat.BuildRecallQuery(null, source.title, source.selftext)), MemoryMaxChars);
        var continuity = BuildEpisodeContinuityContext();
        var sourceJson = BuildPromptSourceJson(source);
        var threadMaterial = LimitText(source.threadContext, 0);

        var resolver = new PromptResolver(generator.ManagerContext, "Reddit Source", "Pitch Candidate");
        var prompt = await resolver.Resolve(
            sourceJson,
            threadMaterial,
            actors,
            memory,
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            continuity);

        var output = await LLM.CompleteAsync(prompt, chat, true);
        var evaluation = await EvaluateAsync(output, source, actors, memory, continuity);
        if (!PitchCandidateEvaluator.IsApproved(evaluation))
        {
            OperatorTelemetry.RecordEvent(
                "pitch_rejected",
                $"Rejected Reddit pitch for {source.id}: {PitchCandidateEvaluator.GetReason(evaluation)}",
                context,
                context.Key);
            return null;
        }

        var candidate = PitchCandidateFactory.FromText(
            output,
            source,
            generator.slug,
            discordChannelKey,
            expirationMinutes,
            PitchCandidateEvaluator.GetReason(evaluation));
        candidate.discordColor = ResolvePitchColor(candidate);

        return candidate;
    }

    private async Task<string> EvaluateAsync(string pitch, RedditPostSource source, string actors, string memory, string continuity)
    {
        var resolver = new PromptResolver(generator.ManagerContext, "Reddit Source", "Pitch Evaluator");
        var prompt = await resolver.Resolve(
            BuildPromptSourceJson(source),
            LimitText(source.threadContext, EvaluatorThreadMaxChars),
            LimitActorsForEvaluation(pitch, actors),
            LimitText(memory, EvaluatorMemoryMaxChars),
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            pitch,
            LimitText(continuity, EvaluatorContinuityMaxChars));

        return await LLM.CompleteAsync(prompt, new Chat(new Idea(pitch), generator.ManagerContext), LlmProfile.Utility);
    }

    private int ResolvePitchColor(PitchCandidate candidate)
    {
        var cast = PitchCandidateText.GetCast(candidate);
        if (string.IsNullOrWhiteSpace(cast))
            return 0;

        var names = cast
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name));

        foreach (var name in names)
        {
            var actor = context.Actors
                .FirstOrDefault(a => a != null && (
                    string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    (a.Aliases?.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) ?? false)));
            if (actor != null)
                return actor.Color1.ToDiscordColor();
        }

        return 0;
    }

    private string BuildEpisodeContinuityContext()
    {
        if (ContinuityEpisodeLimit <= 0)
            return "No recent episode continuity requested.";

        var episodes = new List<EpisodeContinuityItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var episode in OperatorTelemetry.GetRecentEpisodes(ContinuityTelemetryScanLimit)
            .Where(IsCurrentContextEpisode)
            .Where(HasContinuityText))
        {
            if (episode == null || string.IsNullOrWhiteSpace(episode.slug) || seen.Contains(episode.slug))
                continue;

            episodes.Add(new EpisodeContinuityItem
            {
                slug = episode.slug,
                title = episode.title,
                synopsis = episode.synopsis,
                status = episode.status
            });
            seen.Add(episode.slug);
            if (episodes.Count >= ContinuityEpisodeLimit)
                break;
        }

        foreach (var episode in LoadVaultContinuityOutputs())
        {
            if (episode == null || string.IsNullOrWhiteSpace(episode.slug) || seen.Contains(episode.slug))
                continue;

            episodes.Add(episode);
            seen.Add(episode.slug);
            if (episodes.Count >= ContinuityEpisodeLimit)
                break;
        }

        if (episodes.Count == 0)
            return "No recent episode continuity found.";

        return LimitText(string.Join("\n", episodes
            .Take(ContinuityEpisodeLimit)
            .Select(episode =>
            {
                var title = string.IsNullOrWhiteSpace(episode.title) ? episode.slug : episode.title;
                var synopsis = string.IsNullOrWhiteSpace(episode.synopsis) ? "No synopsis recorded." : episode.synopsis.Trim();
                return $"- {title}: {synopsis}";
            })), ContinuityMaxChars);
    }

    private string BuildPromptSourceJson(RedditPostSource source)
    {
        var sourceObject = JObject.FromObject(source);
        sourceObject.Remove(nameof(RedditPostSource.threadContext));
        sourceObject[nameof(RedditPostSource.selftext)] = LimitText(source.selftext, SourceSelftextMaxChars);
        return sourceObject.ToString(Formatting.Indented);
    }

    private string LimitActorsForEvaluation(string pitch, string actors)
    {
        if (EvaluatorActorLimit <= 0 || string.IsNullOrWhiteSpace(actors))
            return actors;

        var lines = actors
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var selected = lines
            .Where(line => IsActorMentionedInPitch(line, pitch))
            .Concat(lines)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(EvaluatorActorLimit);

        return string.Join("\n", selected);
    }

    private static bool IsActorMentionedInPitch(string actorLine, string pitch)
    {
        if (string.IsNullOrWhiteSpace(actorLine) || string.IsNullOrWhiteSpace(pitch))
            return false;

        var actor = actorLine.Trim().TrimStart('-').Trim();
        return actor.Length > 0 && pitch.IndexOf(actor, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string LimitText(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var trimmed = text.Trim();
        if (maxChars <= 0 || trimmed.Length <= maxChars)
            return trimmed;
        return trimmed.Substring(0, Math.Max(0, maxChars - 1)) + "…";
    }

    private bool IsCurrentContextEpisode(EpisodeRecord episode)
    {
        if (episode == null)
            return false;

        var currentKey = context?.Key;
        if (!string.IsNullOrWhiteSpace(currentKey))
            return string.Equals(episode.channelKey, currentKey, StringComparison.OrdinalIgnoreCase);

        var currentName = context?.Name;
        return !string.IsNullOrWhiteSpace(currentName) &&
            string.Equals(episode.context, currentName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasContinuityText(EpisodeRecord episode)
    {
        return episode != null &&
            (!string.IsNullOrWhiteSpace(episode.title) || !string.IsNullOrWhiteSpace(episode.synopsis));
    }

    private IEnumerable<EpisodeContinuityItem> LoadVaultContinuityOutputs()
    {
        foreach (var file in EnumerateContinuityOutputFiles())
        {
            string text;
            try
            {
                var info = new FileInfo(file);
                if (info.Length <= 0 || info.Length > MaxContinuityOutputBytes)
                    continue;

                text = File.ReadAllText(file).Trim();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"RedditPitchCandidateService.LoadVaultContinuityOutputs failed for '{file}': {e.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            yield return new EpisodeContinuityItem
            {
                slug = Path.GetFileNameWithoutExtension(file),
                title = Path.GetFileNameWithoutExtension(file),
                synopsis = text,
                status = "vault-output"
            };
        }
    }

    private IEnumerable<string> EnumerateContinuityOutputFiles()
    {
        return GetContinuityOutputFolders()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .SelectMany(folder =>
            {
                try
                {
                    return Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"RedditPitchCandidateService failed to list continuity outputs in '{folder}': {e.Message}");
                    return Array.Empty<string>();
                }
            })
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(ContinuityEpisodeLimit)
            .Select(file => file.FullName);
    }

    private IEnumerable<string> GetContinuityOutputFolders()
    {
        foreach (var vaultContext in GetVaultContextNames())
        {
            var outputRoot = Path.Combine(PromptResolver.BasePath, vaultContext, PromptResolver.BaseOutputPath);
            yield return Path.Combine(outputRoot, generator.name, "Episode To Episode Continuity");
            yield return Path.Combine(outputRoot, "Defaults", "Episode To Episode Continuity");
            yield return Path.Combine(outputRoot, generator.name, "Continuity");
        }
    }

    private IEnumerable<string> GetVaultContextNames()
    {
        if (!string.IsNullOrWhiteSpace(context?.Name))
            yield return context.Name;
        if (!string.IsNullOrWhiteSpace(context?.Key))
            yield return context.Key;
    }

    private sealed class EpisodeContinuityItem
    {
        public string slug;
        public string title;
        public string synopsis;
        public string status;
    }
}
