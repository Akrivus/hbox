using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using UnityEngine;

public class LLM : MonoBehaviour, IConfigurable<OpenAIConfigs>
{
    public static string OPENAI_API_KEY = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    public static string OPENAI_API_URI = "https://api.openai.com";

    public static bool USE_EMBEDDINGS = true;
    public static string EMBEDDING_MODEL = "text-embedding-3-small";

    public static string SLOW_MODEL = "gpt-5-mini";
    public static string FAST_MODEL = "gpt-5-nano";
    public static Dictionary<LlmProfile, string> MODEL_PROFILES = new Dictionary<LlmProfile, string>();

    public static OpenAIClient API => _api ??= new OpenAIClient(new OpenAIAuthentication(OPENAI_API_KEY), new OpenAISettings(OPENAI_API_URI));
    private static OpenAIClient _api;
    private static int maxConcurrentRequests = 2;
    private static readonly object requestGateSync = new object();
    private static SemaphoreSlim requestGate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);

    public void Configure(OpenAIConfigs c)
    {
        OPENAI_API_URI = c.ApiUri;
        OPENAI_API_KEY = c.ApiKey;

        SLOW_MODEL = c.SlowModel;
        FAST_MODEL = c.FastModel;
        MODEL_PROFILES = BuildModelProfiles(c);
        ConfigureConcurrency(c.MaxConcurrentRequests);

        LlmCallTelemetry.Configure(c.PersistUsage, c.UsageLogPath, c.ModelPrices, c.Budgets);

        USE_EMBEDDINGS = c.UseEmbeddings;
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(OpenAIConfigs), "openai", (_config) => Configure((OpenAIConfigs)_config));
    }

    private static int? RemainingRequests;
    private static int? RemainingTokens;
    private static TimeSpan ResetRequestsTimespan;

    public static async Task<string> ChatAsync(Chat chat, List<Message> messages, bool fast = false, PromptResolver prompt = null, int attempts = 0)
    {
        return await ChatAsync(chat, messages, fast ? LlmProfile.Fast : LlmProfile.Slow, prompt, attempts);
    }

    public static async Task<string> ChatAsync(Chat chat, List<Message> messages, LlmProfile profile, PromptResolver prompt = null, int attempts = 0)
    {
        var text = "";
        if (attempts > 5) return text;

        var model = ResolveModel(profile);
        var stopwatch = Stopwatch.StartNew();
        var inputChars = EstimateMessageChars(messages);
        var inputTokens = inputChars / 3;
        var caller = LlmCallTelemetry.CaptureCaller();

        try
        {
            if (inputTokens > RemainingTokens || RemainingRequests <= 1)
            {
                var reset = ResetRequestsTimespan.TotalSeconds;
                UnityEngine.Debug.LogWarning($"OpenAI rate limit reached. Waiting {reset} seconds.");
                await Task.Delay((int)reset * 1000);
            }

            if (prompt != null)
                await prompt.SaveInput();

            ChatResponse request;
            await requestGate.WaitAsync();
            try
            {
                request = await API.ChatEndpoint.GetCompletionAsync(new ChatRequest(messages, model));
            }
            finally
            {
                requestGate.Release();
            }

            RemainingRequests = request.RemainingRequests;
            RemainingTokens = request.RemainingTokens;
            ResetRequestsTimespan = request.ResetRequestsTimespan;

            var response = request.FirstChoice;
            if (response.FinishReason != "stop")
                throw new Exception(response.FinishDetails);
            messages.Add(response.Message);

            text = response.Message.Content.ToString();

            if (prompt != null)
                await prompt.SaveOutput(text);

            stopwatch.Stop();
            RecordCall(chat, prompt, profile, request, caller, attempts, true, inputChars, text, stopwatch.ElapsedMilliseconds, string.Empty);
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            RecordCall(chat, prompt, profile, null, caller, attempts, false, inputChars, string.Empty, stopwatch.ElapsedMilliseconds, e.Message, model);
            UnityEngine.Debug.LogError(e.Message);
            UnityEngine.Debug.LogError(e.StackTrace);
            await Task.Delay(1000);
            return await ChatAsync(chat, messages, profile, prompt, ++attempts);
        }
        return text;
    }

    public static async Task<string> CompleteAsync(PromptResolver prompt, Chat chat, bool fast = false)
    {
        return await CompleteAsync(prompt, chat, fast ? LlmProfile.Fast : LlmProfile.Slow);
    }

    public static async Task<string> CompleteAsync(PromptResolver prompt, Chat chat, LlmProfile profile)
    {
        if (!prompt.Resolved)
            throw new Exception("Prompt not resolved. Call Resolve() first.");
        return await ChatAsync(chat, new List<Message> { new Message(Role.User, prompt.Text) }, profile, prompt);
    }

    public static async Task<double[]> EmbedAsync(string text, int dimensions = 1536)
    {
        if (!USE_EMBEDDINGS || string.IsNullOrEmpty(text))
            return new double[0];

        var stopwatch = Stopwatch.StartNew();
        var caller = LlmCallTelemetry.CaptureCaller();

        try
        {
            double[] embedding;
            await requestGate.WaitAsync();
            try
            {
                var request = await API.EmbeddingsEndpoint.CreateEmbeddingAsync(new EmbeddingsRequest(text, EMBEDDING_MODEL, "me", dimensions));
                embedding = request.Data.FirstOrDefault().Embedding.ToArray();
            }
            finally
            {
                requestGate.Release();
            }
            stopwatch.Stop();
            RecordMeteredUsage(ChatManagerContext.Current, new LlmCallRecord
            {
                timestamp = DateTimeOffset.Now.ToString("O"),
                channelKey = ChatManagerContext.Current?.Key ?? string.Empty,
                context = ChatManagerContext.Current?.Name ?? string.Empty,
                templateName = $"{caller.type}.{caller.member}",
                profile = "Embedding",
                model = EMBEDDING_MODEL,
                callerType = caller.type,
                callerMember = caller.member,
                success = true,
                inputChars = text.Length,
                estimatedInputTokens = EstimateTokens(text),
                promptTokens = EstimateTokens(text),
                totalTokens = EstimateTokens(text),
                billableUnitName = "tokens",
                billableUnits = EstimateTokens(text),
                usageType = "embedding",
                durationMs = stopwatch.ElapsedMilliseconds
            });
            return embedding;
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            RecordMeteredUsage(ChatManagerContext.Current, new LlmCallRecord
            {
                timestamp = DateTimeOffset.Now.ToString("O"),
                channelKey = ChatManagerContext.Current?.Key ?? string.Empty,
                context = ChatManagerContext.Current?.Name ?? string.Empty,
                templateName = $"{caller.type}.{caller.member}",
                profile = "Embedding",
                model = EMBEDDING_MODEL,
                callerType = caller.type,
                callerMember = caller.member,
                success = false,
                inputChars = text.Length,
                estimatedInputTokens = EstimateTokens(text),
                billableUnitName = "tokens",
                billableUnits = EstimateTokens(text),
                usageType = "embedding",
                durationMs = stopwatch.ElapsedMilliseconds,
                error = e.Message
            });
            UnityEngine.Debug.LogError(e.Message);
            return new double[0];
        }
    }

    private static Dictionary<LlmProfile, string> BuildModelProfiles(OpenAIConfigs config)
    {
        var fastModel = string.IsNullOrWhiteSpace(config.FastModel) ? FAST_MODEL : config.FastModel;
        var slowModel = string.IsNullOrWhiteSpace(config.SlowModel) ? SLOW_MODEL : config.SlowModel;
        var profiles = new Dictionary<LlmProfile, string>
        {
            { LlmProfile.Slow, slowModel },
            { LlmProfile.Fast, fastModel },
            { LlmProfile.Utility, fastModel },
            { LlmProfile.PostProcess, fastModel },
            { LlmProfile.Sentiment, fastModel },
            { LlmProfile.Dialogue, slowModel },
            { LlmProfile.SceneReasoning, slowModel }
        };

        if (config.ModelProfiles == null)
            return profiles;

        foreach (var entry in config.ModelProfiles)
        {
            if (Enum.TryParse<LlmProfile>(entry.Key, true, out var profile) && !string.IsNullOrWhiteSpace(entry.Value))
                profiles[profile] = entry.Value;
        }

        return profiles;
    }

    private static string ResolveModel(LlmProfile profile)
    {
        if (MODEL_PROFILES != null && MODEL_PROFILES.TryGetValue(profile, out var model) && !string.IsNullOrWhiteSpace(model))
            return model;
        return profile == LlmProfile.Fast || profile == LlmProfile.Utility || profile == LlmProfile.PostProcess || profile == LlmProfile.Sentiment
            ? FAST_MODEL
            : SLOW_MODEL;
    }

    private static void ConfigureConcurrency(int maxConcurrent)
    {
        var normalized = Math.Max(1, maxConcurrent);
        lock (requestGateSync)
        {
            if (normalized == maxConcurrentRequests)
                return;

            maxConcurrentRequests = normalized;
            requestGate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        }
    }

    private static int EstimateMessageChars(List<Message> messages)
    {
        return messages?.Sum(m => m?.Content?.ToString()?.Length ?? 0) ?? 0;
    }

    private static int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : text.Length / 3;
    }

    private static void RecordCall(Chat chat, PromptResolver prompt, LlmProfile profile, ChatResponse response, LlmCaller caller, int attempts, bool success, int inputChars, string output, long durationMs, string error, string fallbackModel = null)
    {
        var usage = response?.Usage;
        var promptTokens = usage?.PromptTokens ?? 0;
        var completionTokens = usage?.CompletionTokens ?? 0;
        var totalTokens = usage?.TotalTokens ?? promptTokens + completionTokens;
        var promptPart = prompt?.Part ?? string.Empty;

        var alert = LlmCallTelemetry.Record(new LlmCallRecord
        {
            timestamp = DateTimeOffset.Now.ToString("O"),
            channelKey = chat?.ManagerContext?.Key ?? chat?.Key ?? string.Empty,
            context = chat?.ManagerContext?.Name ?? string.Empty,
            episodeSlug = chat?.FileName ?? chat?.Idea?.Slug ?? string.Empty,
            promptPart = promptPart,
            promptPath = prompt?.Path ?? string.Empty,
            promptUrl = ServerSource.GetVaultUrl(prompt?.Path),
            inputPath = prompt?.InputPath ?? string.Empty,
            inputUrl = ServerSource.GetVaultUrl(prompt?.InputPath),
            outputPath = prompt?.Output?.Path ?? string.Empty,
            outputUrl = ServerSource.GetVaultUrl(prompt?.Output?.Path),
            templateName = string.IsNullOrWhiteSpace(promptPart) ? $"{caller.type}.{caller.member}" : promptPart,
            profile = profile.ToString(),
            model = response?.Model ?? fallbackModel ?? string.Empty,
            responseId = response?.Id ?? string.Empty,
            serviceTier = response?.ServiceTier ?? string.Empty,
            systemFingerprint = response?.SystemFingerprint ?? string.Empty,
            callerType = caller.type,
            callerMember = caller.member,
            attempt = attempts,
            success = success,
            inputChars = inputChars,
            outputChars = output?.Length ?? 0,
            estimatedInputTokens = inputChars / 3,
            estimatedOutputTokens = EstimateTokens(output),
            promptTokens = promptTokens,
            completionTokens = completionTokens,
            totalTokens = totalTokens,
            cachedPromptTokens = usage?.PromptTokensDetails?.CachedTokens ?? 0,
            reasoningTokens = usage?.CompletionTokensDetails?.ReasoningTokens ?? 0,
            promptAudioTokens = usage?.PromptTokensDetails?.AudioTokens ?? 0,
            completionAudioTokens = usage?.CompletionTokensDetails?.AudioTokens ?? 0,
            promptTextTokens = usage?.PromptTokensDetails?.TextTokens ?? 0,
            completionTextTokens = usage?.CompletionTokensDetails?.TextTokens ?? 0,
            promptImageTokens = usage?.PromptTokensDetails?.ImageTokens ?? 0,
            durationMs = durationMs,
            error = error ?? string.Empty
        });
        PublishBudgetAlert(chat?.ManagerContext, alert);
    }

    public static void RecordMeteredUsage(ChatManagerContext context, LlmCallRecord record)
    {
        if (record == null)
            return;

        var alert = LlmCallTelemetry.Record(record);
        PublishBudgetAlert(context, alert);
    }

    private static void PublishBudgetAlert(ChatManagerContext context, LlmBudgetAlertRecord alert)
    {
        if (alert == null)
            return;

        OperatorTelemetry.RecordEvent("llm_budget_warning", alert.message, context, context?.Key, alert.episodeSlug);

        if (alert.enableUiWarning && context != null)
            UiEventBus.PublishError(context, alert.message, 20);

        if (!alert.enableDiscordWarning)
            return;

        if (DiscordManager.Webhooks == null || DiscordManager.Webhooks.Count == 0)
            return;

        var channel = string.IsNullOrWhiteSpace(alert.discordChannel)
            ? DiscordManager.GetStreamChannel(context)
            : alert.discordChannel;
        DiscordManager.PutInQueue(channel, new DiscordWebhookMessage(alert.message, "HBOx Budget", null));
    }
}

public enum LlmProfile
{
    Slow,
    Fast,
    Utility,
    PostProcess,
    Sentiment,
    Dialogue,
    SceneReasoning
}


public static class LlmCallTelemetry
{
    private static readonly object sync = new object();
    private static readonly List<LlmCallRecord> calls = new List<LlmCallRecord>();
    private static readonly Dictionary<string, LlmModelPrice> modelPrices = new Dictionary<string, LlmModelPrice>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> budgetStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private const int MaxCalls = 1000;
    private static bool persistUsage = true;
    private static string usageLogPath = "Logs/llm-usage";
    private static LlmBudgetPolicy budgetPolicy = new LlmBudgetPolicy();

    public static void Configure(bool persist, string logPath, Dictionary<string, LlmModelPrice> prices, LlmBudgetPolicy policy)
    {
        lock (sync)
        {
            persistUsage = persist;
            usageLogPath = string.IsNullOrWhiteSpace(logPath) ? "Logs/llm-usage" : logPath;
            budgetPolicy = policy ?? new LlmBudgetPolicy();
            modelPrices.Clear();

            if (prices == null)
                return;

            foreach (var entry in prices)
                if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                    modelPrices[entry.Key] = entry.Value;
        }
    }

    public static LlmBudgetAlertRecord Record(LlmCallRecord record)
    {
        Record(record, out var alert);
        return alert;
    }

    private static void Record(LlmCallRecord record, out LlmBudgetAlertRecord alert)
    {
        alert = null;
        if (record == null)
            return;

        lock (sync)
        {
            ApplyPricing(record);
            calls.Add(record);
            if (calls.Count > MaxCalls)
                calls.RemoveRange(0, calls.Count - MaxCalls);
            PersistUnsafe(record);
            alert = EvaluateBudgetUnsafe(record);
        }
    }

    public static IReadOnlyList<LlmCallRecord> GetRecent(int limit = 100, string promptPart = null, string profile = null, string model = null, string callerType = null, string channelKey = null)
    {
        lock (sync)
        {
            return calls
                .Where(c => Matches(c.promptPart, promptPart))
                .Where(c => Matches(c.profile, profile))
                .Where(c => Matches(c.model, model))
                .Where(c => Matches(c.callerType, callerType))
                .Where(c => Matches(c.channelKey, channelKey))
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, 500))
                .ToList();
        }
    }

    public static IReadOnlyList<LlmCallSummaryRecord> GetSummary(int limit = 1000)
    {
        return GetBreakdown("templateName", limit);
    }

    public static IReadOnlyList<LlmCallSummaryRecord> GetBreakdown(string groupBy, int limit = 1000)
    {
        lock (sync)
        {
            return BuildBreakdown(calls
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, MaxCalls)), groupBy);
        }
    }

    public static LlmBudgetBreakdownRecord GetBudgetBreakdown(int limit = 1000)
    {
        lock (sync)
        {
            var recent = calls
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, MaxCalls))
                .ToList();

            return BuildBudgetBreakdown(recent, "session", null, null, null);
        }
    }

    public static LlmBudgetBreakdownRecord GetUsageBreakdown(string range, int limit = 5000)
    {
        lock (sync)
        {
            var recent = ResolveUsageRecordsUnsafe(range, out var normalizedRange, out var from, out var to)
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, 10000))
                .ToList();

            return BuildBudgetBreakdown(recent, normalizedRange, null, from, to);
        }
    }

    public static IReadOnlyList<LlmCallRecord> GetUsageCalls(string range, int limit = 1000)
    {
        lock (sync)
        {
            return ResolveUsageRecordsUnsafe(range, out _, out _, out _)
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, 5000))
                .ToList();
        }
    }

    public static IReadOnlyList<LlmCallRecord> GetPersistedCalls(DateTime date, int limit = 1000)
    {
        lock (sync)
        {
            return LoadPersistedUnsafe(date)
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, 5000))
                .ToList();
        }
    }

    public static LlmBudgetBreakdownRecord GetPersistedBudgetBreakdown(DateTime date, int limit = 5000)
    {
        lock (sync)
        {
            var recent = LoadPersistedUnsafe(date)
                .OrderByDescending(c => c.timestamp)
                .Take(NormalizeLimit(limit, 10000))
                .ToList();

            return BuildBudgetBreakdown(recent, "day", date.ToString("yyyy-MM-dd"), date.Date, date.Date);
        }
    }

    public static LlmCaller CaptureCaller()
    {
        var trace = new StackTrace();
        for (var i = 1; i < trace.FrameCount; i++)
        {
            var method = trace.GetFrame(i)?.GetMethod();
            var type = method?.DeclaringType;
            if (method == null || type == null)
                continue;

            if (type == typeof(LLM) || type == typeof(LlmCallTelemetry))
                continue;

            return new LlmCaller
            {
                type = type.Name,
                member = method.Name
            };
        }

        return new LlmCaller
        {
            type = string.Empty,
            member = string.Empty
        };
    }

    private static bool Matches(string value, string filter)
    {
        return string.IsNullOrWhiteSpace(filter) || string.Equals(value ?? string.Empty, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeLimit(int requested, int max)
    {
        if (requested < 1)
            return 100;
        return Math.Min(requested, max);
    }

    private static IReadOnlyList<LlmCallSummaryRecord> BuildBreakdown(IEnumerable<LlmCallRecord> source, string groupBy)
    {
        return source
            .GroupBy(c => GetGroupKey(c, groupBy))
            .Select(g => new LlmCallSummaryRecord
            {
                key = g.Key,
                groupBy = NormalizeGroupBy(groupBy),
                callCount = g.Count(),
                failedCallCount = g.Count(c => !c.success),
                promptTokens = g.Sum(c => c.promptTokens),
                completionTokens = g.Sum(c => c.completionTokens),
                totalTokens = g.Sum(c => c.totalTokens),
                cachedPromptTokens = g.Sum(c => c.cachedPromptTokens),
                reasoningTokens = g.Sum(c => c.reasoningTokens),
                billableUnitName = GetBillableUnitName(g),
                billableUnits = g.Sum(c => c.billableUnits),
                inputCostUsd = g.Sum(c => c.inputCostUsd),
                cachedInputCostUsd = g.Sum(c => c.cachedInputCostUsd),
                outputCostUsd = g.Sum(c => c.outputCostUsd),
                totalCostUsd = g.Sum(c => c.totalCostUsd),
                estimatedInputTokens = g.Sum(c => c.estimatedInputTokens),
                estimatedOutputTokens = g.Sum(c => c.estimatedOutputTokens),
                estimatedTotalTokens = g.Sum(c => c.estimatedInputTokens + c.estimatedOutputTokens),
                totalDurationMs = g.Sum(c => c.durationMs),
                models = g.Select(c => c.model).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().OrderBy(m => m).ToList(),
                profiles = g.Select(c => c.profile).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().OrderBy(p => p).ToList(),
                channelKeys = g.Select(c => c.channelKey).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().OrderBy(k => k).ToList(),
                templateNames = g.Select(c => c.templateName).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList()
            })
            .OrderByDescending(s => s.totalCostUsd)
            .ThenByDescending(s => s.totalTokens > 0 ? s.totalTokens : s.estimatedTotalTokens)
            .ThenByDescending(s => s.callCount)
            .ToList();
    }

    private static LlmBudgetBreakdownRecord BuildBudgetBreakdown(IReadOnlyList<LlmCallRecord> recent, string range, string date, DateTime? from, DateTime? to)
    {
        foreach (var record in recent)
            ApplyPricing(record);
        foreach (var record in calls)
            ApplyPricing(record);

        return new LlmBudgetBreakdownRecord
        {
            generatedAt = DateTimeOffset.Now.ToString("O"),
            range = range ?? string.Empty,
            date = date ?? string.Empty,
            from = from?.ToString("yyyy-MM-dd") ?? string.Empty,
            to = to?.ToString("yyyy-MM-dd") ?? string.Empty,
            callCount = recent.Count,
            promptTokens = recent.Sum(c => c.promptTokens),
            completionTokens = recent.Sum(c => c.completionTokens),
            totalTokens = recent.Sum(c => c.totalTokens),
            cachedPromptTokens = recent.Sum(c => c.cachedPromptTokens),
            reasoningTokens = recent.Sum(c => c.reasoningTokens),
            estimatedTotalTokens = recent.Sum(c => c.estimatedInputTokens + c.estimatedOutputTokens),
            inputCostUsd = recent.Sum(c => c.inputCostUsd),
            cachedInputCostUsd = recent.Sum(c => c.cachedInputCostUsd),
            outputCostUsd = recent.Sum(c => c.outputCostUsd),
            totalCostUsd = recent.Sum(c => c.totalCostUsd),
            sessionCostUsd = calls.Sum(c => c.totalCostUsd),
            sessionCallCount = calls.Count,
            policy = BuildPolicySnapshot(),
            byChannel = BuildBreakdown(recent, "channelKey"),
            byContext = BuildBreakdown(recent, "context"),
            byTemplate = BuildBreakdown(recent, "templateName"),
            byCaller = BuildBreakdown(recent, "callerType"),
            byProfile = BuildBreakdown(recent, "profile"),
            byModel = BuildBreakdown(recent, "model"),
            byUsageType = BuildBreakdown(recent, "usageType")
        };
    }

    private static void ApplyPricing(LlmCallRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.model))
            return;

        if (!TryGetModelPrice(record.model, out var price) || price == null)
            return;

        if (record.billableUnits > 0)
        {
            record.inputCostUsd = record.billableUnits * price.InputPerMillion / 1000000d;
            record.cachedInputCostUsd = 0;
            record.outputCostUsd = 0;
            record.totalCostUsd = record.inputCostUsd;
            record.priceFound = true;
            return;
        }

        var cachedTokens = Math.Max(0, record.cachedPromptTokens);
        var uncachedPromptTokens = Math.Max(0, record.promptTokens - cachedTokens);
        record.inputCostUsd = uncachedPromptTokens * price.InputPerMillion / 1000000d;
        record.cachedInputCostUsd = cachedTokens * price.CachedInputPerMillion / 1000000d;
        record.outputCostUsd = Math.Max(0, record.completionTokens) * price.OutputPerMillion / 1000000d;
        record.totalCostUsd = record.inputCostUsd + record.cachedInputCostUsd + record.outputCostUsd;
        record.priceFound = true;
    }

    private static bool TryGetModelPrice(string model, out LlmModelPrice price)
    {
        price = null;
        if (string.IsNullOrWhiteSpace(model))
            return false;

        if (modelPrices.TryGetValue(model, out price) && price != null)
            return true;

        var bestMatch = modelPrices
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Key) &&
                entry.Value != null &&
                model.StartsWith(entry.Key + "-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Key.Length)
            .FirstOrDefault();

        if (bestMatch.Value == null)
            return false;

        price = bestMatch.Value;
        return true;
    }

    private static LlmBudgetAlertRecord EvaluateBudgetUnsafe(LlmCallRecord record)
    {
        var memoryDailyCost = calls
            .Where(c => IsSameLocalDate(c.timestamp, DateTime.Today))
            .Sum(c => c.totalCostUsd);
        var persistedDailyCost = persistUsage
            ? LoadPersistedUnsafe(DateTime.Today).Sum(c => c.totalCostUsd)
            : 0;
        var dailyCost = Math.Max(memoryDailyCost, persistedDailyCost);

        var dailyAlert = EvaluateBudgetScopeUnsafe(
            "daily",
            DateTime.Today.ToString("yyyy-MM-dd"),
            dailyCost,
            budgetPolicy.DailyUsd,
            record);
        if (dailyAlert != null)
            return dailyAlert;

        if (string.IsNullOrWhiteSpace(record.episodeSlug))
            return null;

        var episodeCost = calls
            .Where(c => string.Equals(c.episodeSlug, record.episodeSlug, StringComparison.OrdinalIgnoreCase))
            .Sum(c => c.totalCostUsd);
        return EvaluateBudgetScopeUnsafe("episode", record.episodeSlug, episodeCost, budgetPolicy.PerEpisodeUsd, record);
    }

    private static bool IsSameLocalDate(string timestamp, DateTime date)
    {
        return TryGetLocalDate(timestamp, out var parsedDate) && parsedDate == date.Date;
    }

    private static LlmBudgetAlertRecord EvaluateBudgetScopeUnsafe(string scope, string key, double costUsd, double limitUsd, LlmCallRecord record)
    {
        if (limitUsd <= 0 || costUsd <= 0)
            return null;

        var warnAt = budgetPolicy.WarnAtPercent <= 0 ? 80 : budgetPolicy.WarnAtPercent;
        var ratio = costUsd / limitUsd;
        var state = ratio >= 1 ? "exceeded" : ratio * 100 >= warnAt ? "warning" : "ok";
        if (state == "ok")
            return null;

        var stateKey = $"{scope}:{key}";
        if (budgetStates.TryGetValue(stateKey, out var prior) && string.Equals(prior, state, StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(prior, "exceeded", StringComparison.OrdinalIgnoreCase))
            return null;

        budgetStates[stateKey] = state;
        var percent = ratio * 100;
        return new LlmBudgetAlertRecord
        {
            scope = scope,
            key = key,
            state = state,
            currentUsd = costUsd,
            limitUsd = limitUsd,
            percent = percent,
            message = $"LLM {scope} budget {state}: ${costUsd:0.0000} of ${limitUsd:0.00} ({percent:0.#}%). Last stage: {record.templateName}.",
            channelKey = record.channelKey,
            context = record.context,
            episodeSlug = record.episodeSlug,
            enableUiWarning = budgetPolicy.EnableUiWarnings,
            enableDiscordWarning = budgetPolicy.EnableDiscordWarnings,
            discordChannel = budgetPolicy.DiscordChannel
        };
    }

    private static LlmBudgetPolicySnapshot BuildPolicySnapshot()
    {
        return new LlmBudgetPolicySnapshot
        {
            dailyUsd = budgetPolicy.DailyUsd,
            perEpisodeUsd = budgetPolicy.PerEpisodeUsd,
            warnAtPercent = budgetPolicy.WarnAtPercent,
            enableUiWarnings = budgetPolicy.EnableUiWarnings,
            enableDiscordWarnings = budgetPolicy.EnableDiscordWarnings,
            discordChannel = budgetPolicy.DiscordChannel ?? string.Empty
        };
    }

    private static void PersistUnsafe(LlmCallRecord record)
    {
        if (!persistUsage || record == null)
            return;

        try
        {
            Directory.CreateDirectory(usageLogPath);
            var path = Path.Combine(usageLogPath, DateTimeOffset.Now.ToString("yyyy-MM-dd") + ".jsonl");
            File.AppendAllText(path, JsonConvert.SerializeObject(record) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<LlmCallRecord> LoadPersistedUnsafe(DateTime date)
    {
        var path = Path.Combine(usageLogPath, date.ToString("yyyy-MM-dd") + ".jsonl");
        if (!File.Exists(path))
            return Array.Empty<LlmCallRecord>();

        var records = new List<LlmCallRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var record = JsonConvert.DeserializeObject<LlmCallRecord>(line);
                if (record != null)
                {
                    ApplyPricing(record);
                    records.Add(record);
                }
            }
            catch
            {
            }
        }

        return records;
    }

    private static IReadOnlyList<LlmCallRecord> ResolveUsageRecordsUnsafe(string range, out string normalizedRange, out DateTime? from, out DateTime? to)
    {
        normalizedRange = NormalizeUsageRange(range);
        from = null;
        to = null;

        if (normalizedRange == "session")
            return calls.ToList();

        var today = DateTime.Today;
        List<LlmCallRecord> records;
        switch (normalizedRange)
        {
            case "day":
                from = today;
                to = today;
                records = LoadPersistedRangeUnsafe(today, today).ToList();
                break;
            case "week":
                from = today.AddDays(-6);
                to = today;
                records = LoadPersistedRangeUnsafe(from.Value, to.Value).ToList();
                break;
            case "month":
                from = new DateTime(today.Year, today.Month, 1);
                to = today;
                records = LoadPersistedRangeUnsafe(from.Value, to.Value).ToList();
                break;
            case "all":
                records = LoadAllPersistedUnsafe().ToList();
                break;
            default:
                normalizedRange = "session";
                return calls.ToList();
        }

        MergeMemoryCallsUnsafe(records, from, to);
        return records;
    }

    private static string NormalizeUsageRange(string range)
    {
        if (string.IsNullOrWhiteSpace(range))
            return "day";

        switch (range.Trim().ToLowerInvariant())
        {
            case "live":
            case "current":
            case "current-session":
            case "session":
                return "session";
            case "day":
            case "daily":
            case "today":
                return "day";
            case "7d":
            case "weekly":
            case "week":
                return "week";
            case "30d":
            case "monthly":
            case "calendar-month":
            case "month":
                return "month";
            case "365d":
            case "yearly":
            case "year":
                return "all";
            case "all-time":
            case "lifetime":
            case "all":
                return "all";
            default:
                return "day";
        }
    }

    private static IReadOnlyList<LlmCallRecord> LoadPersistedRangeUnsafe(DateTime startDate, DateTime endDate)
    {
        var records = new List<LlmCallRecord>();
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            records.AddRange(LoadPersistedUnsafe(date));
        return records;
    }

    private static IReadOnlyList<LlmCallRecord> LoadAllPersistedUnsafe()
    {
        if (!Directory.Exists(usageLogPath))
            return Array.Empty<LlmCallRecord>();

        var records = new List<LlmCallRecord>();
        foreach (var path in Directory.GetFiles(usageLogPath, "*.jsonl"))
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonConvert.DeserializeObject<LlmCallRecord>(line);
                    if (record != null)
                    {
                        ApplyPricing(record);
                        records.Add(record);
                    }
                }
                catch
                {
                }
            }
        }

        return records;
    }

    private static void MergeMemoryCallsUnsafe(List<LlmCallRecord> records, DateTime? from, DateTime? to)
    {
        var seen = new HashSet<string>(records.Select(GetRecordKey));
        foreach (var call in calls)
        {
            if (!IsWithinDateRange(call.timestamp, from, to))
                continue;

            if (seen.Add(GetRecordKey(call)))
                records.Add(call);
        }
    }

    private static string GetRecordKey(LlmCallRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record?.responseId))
            return record.responseId;
        return $"{record?.timestamp}|{record?.channelKey}|{record?.templateName}|{record?.model}|{record?.totalTokens}";
    }

    private static bool IsWithinDateRange(string timestamp, DateTime? from, DateTime? to)
    {
        if (from == null && to == null)
            return true;
        if (!TryGetLocalDate(timestamp, out var date))
            return false;
        if (from != null && date < from.Value.Date)
            return false;
        return to == null || date <= to.Value.Date;
    }

    private static bool TryGetLocalDate(string timestamp, out DateTime date)
    {
        date = default;
        if (!DateTimeOffset.TryParse(timestamp, out var parsed))
            return false;
        date = parsed.LocalDateTime.Date;
        return true;
    }

    private static string GetGroupKey(LlmCallRecord call, string groupBy)
    {
        switch (NormalizeGroupBy(groupBy))
        {
            case "channelKey":
                return BlankKey(call.channelKey);
            case "context":
                return BlankKey(call.context);
            case "promptPart":
                return BlankKey(call.promptPart);
            case "callerType":
                return BlankKey(call.callerType);
            case "callerMember":
                return BlankKey(call.callerMember);
            case "profile":
                return BlankKey(call.profile);
            case "model":
                return BlankKey(call.model);
            case "usageType":
                return BlankKey(call.usageType, "llm");
            case "templateName":
            default:
                return BlankKey(call.templateName);
        }
    }

    private static string NormalizeGroupBy(string groupBy)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
            return "templateName";

        switch (groupBy.Trim())
        {
            case "channel":
            case "ip":
                return "channelKey";
            case "stage":
            case "template":
            case "prompt":
                return "templateName";
            case "caller":
            case "generator":
                return "callerType";
            case "type":
            case "service":
            case "usage":
                return "usageType";
            default:
                return groupBy.Trim();
        }
    }

    private static string GetBillableUnitName(IEnumerable<LlmCallRecord> records)
    {
        var names = records
            .Select(c => c.billableUnitName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return names.Count == 1 ? names[0] : string.Empty;
    }

    private static string BlankKey(string value, string placeholder = "(unknown)")
    {
        return string.IsNullOrWhiteSpace(value) ? placeholder : value;
    }
}

[Serializable]
public sealed class LlmCallRecord
{
    public string timestamp;
    public string channelKey;
    public string context;
    public string episodeSlug;
    public string promptPart;
    public string promptPath;
    public string promptUrl;
    public string inputPath;
    public string inputUrl;
    public string outputPath;
    public string outputUrl;
    public string templateName;
    public string profile;
    public string model;
    public string responseId;
    public string serviceTier;
    public string systemFingerprint;
    public string callerType;
    public string callerMember;
    public int attempt;
    public bool success;
    public int inputChars;
    public int outputChars;
    public int estimatedInputTokens;
    public int estimatedOutputTokens;
    public int promptTokens;
    public int completionTokens;
    public int totalTokens;
    public int cachedPromptTokens;
    public int reasoningTokens;
    public int promptAudioTokens;
    public int completionAudioTokens;
    public int promptTextTokens;
    public int completionTextTokens;
    public int promptImageTokens;
    public string usageType;
    public string billableUnitName;
    public int billableUnits;
    public bool priceFound;
    public double inputCostUsd;
    public double cachedInputCostUsd;
    public double outputCostUsd;
    public double totalCostUsd;
    public long durationMs;
    public string error;
}

[Serializable]
public sealed class LlmCallSummaryRecord
{
    public string key;
    public string groupBy;
    public int callCount;
    public int failedCallCount;
    public int promptTokens;
    public int completionTokens;
    public int totalTokens;
    public int cachedPromptTokens;
    public int reasoningTokens;
    public string billableUnitName;
    public int billableUnits;
    public double inputCostUsd;
    public double cachedInputCostUsd;
    public double outputCostUsd;
    public double totalCostUsd;
    public int estimatedInputTokens;
    public int estimatedOutputTokens;
    public int estimatedTotalTokens;
    public long totalDurationMs;
    public List<string> models;
    public List<string> profiles;
    public List<string> channelKeys;
    public List<string> templateNames;
}

[Serializable]
public sealed class LlmBudgetBreakdownRecord
{
    public string generatedAt;
    public string range;
    public string date;
    public string from;
    public string to;
    public int callCount;
    public int promptTokens;
    public int completionTokens;
    public int totalTokens;
    public int cachedPromptTokens;
    public int reasoningTokens;
    public int estimatedTotalTokens;
    public double inputCostUsd;
    public double cachedInputCostUsd;
    public double outputCostUsd;
    public double totalCostUsd;
    public double sessionCostUsd;
    public int sessionCallCount;
    public IReadOnlyList<LlmCallSummaryRecord> byUsageType;
    public LlmBudgetPolicySnapshot policy;
    public IReadOnlyList<LlmCallSummaryRecord> byChannel;
    public IReadOnlyList<LlmCallSummaryRecord> byContext;
    public IReadOnlyList<LlmCallSummaryRecord> byTemplate;
    public IReadOnlyList<LlmCallSummaryRecord> byCaller;
    public IReadOnlyList<LlmCallSummaryRecord> byProfile;
    public IReadOnlyList<LlmCallSummaryRecord> byModel;
}

[Serializable]
public sealed class LlmBudgetPolicySnapshot
{
    public double dailyUsd;
    public double perEpisodeUsd;
    public double warnAtPercent;
    public bool enableUiWarnings;
    public bool enableDiscordWarnings;
    public string discordChannel;
}

[Serializable]
public sealed class LlmBudgetAlertRecord
{
    public string scope;
    public string key;
    public string state;
    public double currentUsd;
    public double limitUsd;
    public double percent;
    public string message;
    public string channelKey;
    public string context;
    public string episodeSlug;
    public bool enableUiWarning;
    public bool enableDiscordWarning;
    public string discordChannel;
}

public struct LlmCaller
{
    public string type;
    public string member;
}
