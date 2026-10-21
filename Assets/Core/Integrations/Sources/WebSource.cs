using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Adapters own discovery and history; this class owns scheduling and pitch/Idea delivery.
public abstract class WebSource : MonoBehaviour, IWebSource
{
    [SerializeField] protected ChatGenerator generator;
    public ChatGenerator Generator => generator;
    public event Action OnBatchStart;
    public event Action OnBatchEnd;
    public bool IsConfigured { get; private set; }
    public bool IsBatchRunning { get; private set; }
    public abstract string SourceKey { get; }
    public abstract string PromptRoot { get; }
    protected ChatManagerContext chatManagerContext;
    private PitchCandidateStore pitchStore;
    private WebPitchCandidateService pitchCandidateService;
    private WebSourceConfigs config;
    private WebSourceBatchConfigs batch;
    private Coroutine schedule;
    private bool destroyed;
    private int revision;

    public void BindGenerator(ChatGenerator value) => generator = value;

    protected virtual void Start()
    {
        chatManagerContext = GetComponentInParent<ChatManagerContext>() ?? generator?.ManagerContext ?? ChatManagerContext.Current;
        if (generator == null) generator = chatManagerContext.GetComponentInChildren<ChatGenerator>();
        pitchStore = PitchCandidateStore.GetOrCreate(chatManagerContext, generator);
        pitchCandidateService = new WebPitchCandidateService(chatManagerContext, generator);
        RegisterConfig();
    }

    protected abstract void RegisterConfig();
    public abstract Task<IReadOnlyList<WebSourceDocument>> FetchBatchAsync(int limit);
    public abstract void RememberAccepted(WebSourceDocument source);

    protected void ConfigureWebSource(WebSourceConfigs value)
    {
        var policy = value.Batch ?? value;
        policy.Validate();
        revision++;
        if (schedule != null) StopCoroutine(schedule);
        config = value;
        batch = policy;
        IsConfigured = value.Enabled;
        if (!IsConfigured) return;
        pitchCandidateService.MemoryMaxChars = Math.Max(0, value.PitchMemoryMaxChars);
        pitchCandidateService.ContinuityEpisodeLimit = Math.Max(0, value.PitchContinuityEpisodeLimit);
        pitchCandidateService.ContinuityMaxChars = Math.Max(0, value.PitchContinuityMaxChars);
        pitchCandidateService.SourceSelftextMaxChars = Math.Max(0, value.PitchSourceSelftextMaxChars);
        pitchCandidateService.EvaluatorThreadMaxChars = Math.Max(0, value.PitchEvaluatorThreadMaxChars);
        pitchCandidateService.EvaluatorMemoryMaxChars = Math.Max(0, value.PitchEvaluatorMemoryMaxChars);
        pitchCandidateService.EvaluatorContinuityMaxChars = Math.Max(0, value.PitchEvaluatorContinuityMaxChars);
        pitchCandidateService.EvaluatorActorLimit = Math.Max(0, value.PitchEvaluatorActorLimit);
        // This fallback only applies to legacy pitches without persisted per-pitch thresholds.
        if (SourceKey == "reddit") pitchStore.ConfigureVoting(value.PitchMinimumVotesToQueue, value.PitchApprovalScore);
        schedule = StartCoroutine(Drops());
    }

    public IEnumerator Drops()
    {
        while (!destroyed && IsConfigured)
        {
            var next = batch.NextRun(DateTime.Now);
            UiEventBus.Publish(chatManagerContext, next);
            yield return new WaitUntil(() => destroyed || !IsConfigured ||
                (chatManagerContext.IsActive && !ChatManager.IsPaused && DateTime.Now >= next));
            if (destroyed || !IsConfigured) yield break;
            yield return Drop();
        }
    }

    public void DoDrop()
    {
        if (IsConfigured && !IsBatchRunning) StartCoroutine(Drop());
    }

    public IEnumerator Drop()
    {
        if (!IsConfigured || IsBatchRunning || !chatManagerContext.IsActive || ChatManager.IsPaused) yield break;
        yield return FetchIdeas(batch.IsActive(DateTime.Now)).AsCoroutine();
    }

    public async Task FetchIdeas(bool canAutoApprove = true)
    {
        if (!IsConfigured || IsBatchRunning || destroyed) return;
        IsBatchRunning = true;
        var runRevision = revision;
        var settings = config;
        var policy = batch;
        var accepted = 0;
        var autoRemaining = canAutoApprove ? Math.Max(0, settings.PitchAutoApprovalBatchSize) : 0;
        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            OnBatchStart?.Invoke();
            pitchStore.ResolveFinishedVotes(settings.PitchMinimumVotesToQueue);
            for (var iteration = 0; iteration < policy.BatchIterations && accepted < policy.BatchSizeLimit; iteration++)
            for (var pass = 0; pass < policy.BatchSize && accepted < policy.BatchSizeLimit; pass++)
            {
                if (!CanContinue(runRevision)) return;
                var documents = await FetchBatchAsync(policy.BatchSize);
                foreach (var source in documents)
                {
                    if (!CanContinue(runRevision)) return;
                    if (accepted >= policy.BatchSizeLimit) break;
                    if (source == null || string.IsNullOrWhiteSpace(source.id) || !attempted.Add(source.Identity)) continue;
                    source.logoUrl = settings.LogoUrl ?? SourceBrand.Logo(SourceKey);
                    source.brandColor = settings.BrandColor ?? SourceBrand.Color(SourceKey);
                    var topic = source.title + "\n\n" + source.selftext + "\n\n" + source.threadContext;
                    if (source.market != null) topic += "\n\n" + source.Summary() + "\n\n" + source.GenerationNotes;
                    if (settings.EnablePitchGate)
                    {
                        var candidate = await pitchCandidateService.GenerateAsync(source, topic,
                            ResolvePitchDiscordChannel(settings), settings.PitchExpirationMinutes, PromptRoot);
                        if (!CanContinue(runRevision)) return;
                        if (candidate == null) continue;
                        candidate.minimumVotesToQueue = Math.Max(0, settings.PitchMinimumVotesToQueue);
                        candidate.approvalScore = Math.Max(1, settings.PitchApprovalScore);
                        if (autoRemaining > 0)
                        {
                            pitchStore.QueueCandidate(candidate, "pitch_auto_queued");
                            autoRemaining--;
                        }
                        else PitchDiscordPublisher.Publish(candidate, pitchStore, chatManagerContext);
                        UiEventBus.Publish(chatManagerContext, $"Accepted {source.DisplayName} pitch: {PitchCandidateText.GetTitle(candidate)}");
                    }
                    else
                    {
                        var template = await PromptResolver.Read(generator.ManagerContext, PromptRoot,
                            SourceKey == "polymarket" ? null : "{0}");
                        if (template == null) throw new InvalidOperationException($"Missing Vault prompt: {PromptRoot}.md");
                        var context = source.promptContext;
                        if (!string.IsNullOrWhiteSpace(context)) topic = WebSourcePrompts.Format(context, topic);
                        // Never interpret braces in source material as format placeholders.
                        var prompt = WebSourcePrompts.Format(template, topic, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                        if (!CanContinue(runRevision)) return;
                        generator.AddIdeaToQueue(new Idea(prompt, source.author, source.IdeaSource, source.Identity));
                    }
                    RememberAccepted(source);
                    accepted++;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{SourceKey} batch failed: {e.Message}");
        }
        finally
        {
            IsBatchRunning = false;
            OnBatchEnd?.Invoke();
        }
    }

    private bool CanContinue(int runRevision) => !destroyed && IsConfigured && revision == runRevision &&
        chatManagerContext.IsActive && !ChatManager.IsPaused;

    private string ResolvePitchDiscordChannel(WebSourceConfigs settings) =>
        !string.IsNullOrWhiteSpace(settings.PitchDiscordChannel) && settings.PitchDiscordChannel != "#stream"
            ? settings.PitchDiscordChannel : DiscordManager.GetStreamChannel(chatManagerContext);

    protected virtual void OnDestroy()
    {
        destroyed = true;
        revision++;
        StopAllCoroutines();
    }
}
