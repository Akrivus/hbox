using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

// Discord delivery/deduplication coordinator, not a separate pitch engine.
// Interpretation and evaluation are delegated to the shared PitchCandidateService.
public sealed class DiscordSubmissionIntake
{
    private readonly ChatGenerator generator;
    private readonly PitchCandidateStore store;
    private readonly string historyPath;
    private readonly HashSet<string> seen;
    private readonly bool historyAvailable = true;
    private DateTime nextPollSweep;

    public DiscordSubmissionIntake(ChatGenerator generator)
    {
        this.generator = generator;
        store = PitchCandidateStore.GetOrCreate(generator.ManagerContext, generator);
        historyPath = $"discord-submissions-{generator.slug}.json";
        seen = new HashSet<string>();
        try
        {
            var legacyPath = $"discord-tips-{generator.slug}.json";
            foreach (var path in new[] { legacyPath, historyPath })
                if (File.Exists(path)) seen.UnionWith(JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(path)) ?? new HashSet<string>());
        }
        catch (Exception e)
        {
            historyAvailable = false;
            Debug.LogWarning($"Discord submissions disabled for {generator.slug}: cannot read {historyPath}: {e.Message}");
        }
    }

    public void Tick()
    {
        if (DateTime.UtcNow < nextPollSweep) return;
        nextPollSweep = DateTime.UtcNow.AddSeconds(5);
        store.ResolveFinishedVotes(1); // Each new submission persists its own vote thresholds.
    }

    public async Task SubmitAsync(WebSourceDocument source, DiscordSubmissionChannelConfigs settings, Func<bool> isCurrent,
        Func<PitchCandidate, PitchCandidateStore, Task> publish = null)
    {
        if (!historyAvailable || !isCurrent() || seen.Contains(source.Identity) || store.ContainsSource(source.Identity)) return;
        var service = new PitchCandidateService(generator.ManagerContext, generator) { SourceSelftextMaxChars = 4000 };
        var channel = settings.PitchChannelId ?? DiscordManager.GetStreamChannel(generator.ManagerContext);
        var candidate = await service.GenerateAsync(source, source.selftext, channel, settings.PitchExpirationMinutes, settings.PromptRoot);
        if (!isCurrent()) return;
        if (candidate != null)
        {
            candidate.minimumVotesToQueue = settings.PitchMinimumVotesToQueue;
            candidate.approvalScore = settings.PitchApprovalScore;
            if (publish != null) await publish(candidate, store);
            else PitchDiscordPublisher.Publish(candidate, store, generator.ManagerContext);
        }
        // Record evaluator rejections as well as accepted polls; gateway redelivery is not a new submission.
        seen.Add(source.Identity);
        var temporary = historyPath + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(seen));
        if (File.Exists(historyPath)) File.Replace(temporary, historyPath, null);
        else File.Move(temporary, historyPath);
    }
}
