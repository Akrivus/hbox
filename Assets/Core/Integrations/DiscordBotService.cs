using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Utilities.WebSockets;

public class DiscordBotService : MonoBehaviour, IConfigurable<DiscordConfigs>
{
    private const int DispatchOpcode = 0;
    private const int HeartbeatOpcode = 1;
    private const int IdentifyOpcode = 2;
    private const int ResumeOpcode = 6;
    private const int ReconnectOpcode = 7;
    private const int InvalidSessionOpcode = 9;
    private const int HelloOpcode = 10;
    private const int HeartbeatAckOpcode = 11;

    private const int InteractionPingType = 1;
    private const int ApplicationCommandType = 2;
    private const int ApplicationCommandAutocompleteType = 4;

    private const int ChannelMessageWithSourceResponseType = 4;
    private const int DeferredChannelMessageWithSourceResponseType = 5;
    private const int ApplicationCommandAutocompleteResultType = 8;

    private const int IntentGuilds = 1 << 0;
    private const int IntentGuildMessageReactions = 1 << 10;
    private const int IntentGuildMessages = 1 << 9;
    private const int IntentMessageContent = 1 << 15;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly string ThumbsUpEmoji = char.ConvertFromUtf32(0x1F44D);
    private static readonly string ThumbsDownEmoji = char.ConvertFromUtf32(0x1F44E);

    private bool enableBot;
    private string botToken;
    private string gatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
    private string applicationId;
    private string[] slashCommandGuildIds = Array.Empty<string>();
    private bool enableIdeaCommand = true;
    private Dictionary<string, DiscordSubmissionChannelConfigs> submissionChannels = new Dictionary<string, DiscordSubmissionChannelConfigs>();
    private static readonly HashSet<DiscordBotService> knownBots = new HashSet<DiscordBotService>();
    private static Dictionary<string, DiscordSubmissionChannelConfigs> allSubmissionChannels = new Dictionary<string, DiscordSubmissionChannelConfigs>();
    private readonly HashSet<string> submissionsInFlight = new HashSet<string>();
    private readonly object submissionsLock = new object();
    private int submissionsRevision;
    private DiscordParticipationPolicy participation = new DiscordParticipationPolicy(null);
    private DiscordReactionVotes reactionVotes;

    private CancellationTokenSource lifetimeCts;
    private CancellationTokenSource heartbeatCts;
    private IWebSocket socket;
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    private readonly object usageLock = new object();
    private readonly Dictionary<string, UserIdeaUsage> ideaUsage = new Dictionary<string, UserIdeaUsage>(StringComparer.Ordinal);

    private int? sequence;
    private int heartbeatIntervalMs;
    private bool heartbeatAcknowledged = true;
    private string sessionId;
    private string resumeGatewayUrl;
    private string selfUserId;

    public static DiscordBotService Instance { get; private set; }

    public void Configure(DiscordConfigs config)
    {
        var routes = new Dictionary<string, DiscordSubmissionChannelConfigs>();
        foreach (var pair in config?.SubmissionChannels ?? new Dictionary<string, DiscordSubmissionChannelConfigs>())
        {
            pair.Value?.Validate();
            if (pair.Value?.Enabled == true && !string.IsNullOrWhiteSpace(pair.Key)) routes[pair.Key] = pair.Value;
        }
        submissionChannels = routes;
        knownBots.Add(this);
        RebuildSubmissionRoutes(this);
        enableBot = config?.EnableBot ?? false;
        botToken = config?.BotToken;
        gatewayUrl = string.IsNullOrWhiteSpace(config?.GatewayURL)
            ? "wss://gateway.discord.gg/?v=10&encoding=json"
            : config.GatewayURL;
        applicationId = config?.ApplicationId;
        slashCommandGuildIds = config?.SlashCommandGuildIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        enableIdeaCommand = config?.EnableIdeaCommand ?? true;
        participation.Configure(config);
        reactionVotes = reactionVotes ?? new DiscordReactionVotes(
            Path.Combine(Application.persistentDataPath, "hbox-discord-votes.json"), message => Debug.LogWarning(message));

        if (enableBot && !string.IsNullOrWhiteSpace(botToken))
        {
            StartBot();
            return;
        }

        StopBot();
    }

    private void Start()
    {
        Instance = this;
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(DiscordConfigs), "discord", config => Configure((DiscordConfigs)config));
    }

    private void OnDestroy()
    {
        knownBots.Remove(this);
        RebuildSubmissionRoutes();
        if (Instance == this)
            Instance = null;

        StopBot();
        sendLock.Dispose();
    }

    public void AddDefaultReplayReactions(DiscordPostedMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.id) || string.IsNullOrWhiteSpace(message.channel_id))
            return;
        if (!enableBot || string.IsNullOrWhiteSpace(botToken))
            return;

        _ = AddDefaultReplayReactionsAsync(message);
    }

    public void AddDefaultPitchReactions(DiscordPostedMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.id) || string.IsNullOrWhiteSpace(message.channel_id))
            return;
        if (!enableBot || string.IsNullOrWhiteSpace(botToken))
            return;

        _ = AddDefaultReplayReactionsAsync(message);
    }

    public void PinPitchMessage(DiscordPostedMessage message, IEnumerable<DiscordMessageRef> previousPitchMessages = null)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.id) || string.IsNullOrWhiteSpace(message.channel_id))
            return;
        if (!enableBot || string.IsNullOrWhiteSpace(botToken))
            return;

        _ = PinPitchMessageAsync(message, previousPitchMessages);
    }

    public void PinNowPlayingMessage(DiscordPostedMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.id) || string.IsNullOrWhiteSpace(message.channel_id))
            return;
        if (!enableBot || string.IsNullOrWhiteSpace(botToken))
            return;

        _ = PinNowPlayingMessageAsync(message);
    }

    public void UnpinNowPlayingMessage(DiscordPostedMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.id) || string.IsNullOrWhiteSpace(message.channel_id))
            return;
        if (!enableBot || string.IsNullOrWhiteSpace(botToken))
            return;

        _ = UnpinNowPlayingMessageAsync(message);
    }

    private void StartBot()
    {
        StopBot();
        lifetimeCts = new CancellationTokenSource();
        _ = RunBotLoopAsync(lifetimeCts.Token);
    }

    private void StopBot()
    {
        submissionsRevision++;
        lifetimeCts?.Cancel();
        lifetimeCts?.Dispose();
        lifetimeCts = null;

        StopHeartbeatLoop();

        if (socket != null)
        {
            try
            {
                socket.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"DiscordBotService.StopBot close failed: {e.Message}");
            }

            socket.Dispose();
            socket = null;
        }
    }

    private async Task RunBotLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var activeGatewayUrl = string.IsNullOrWhiteSpace(resumeGatewayUrl) ? gatewayUrl : resumeGatewayUrl;
            WebSocket webSocket = null;

            try
            {
                webSocket = new WebSocket(activeGatewayUrl);
                socket = webSocket;

                webSocket.OnOpen += OnSocketOpened;
                webSocket.OnMessage += OnSocketMessage;
                webSocket.OnError += OnSocketError;
                webSocket.OnClose += OnSocketClosed;

                await webSocket.ConnectAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"DiscordBotService.RunBotLoopAsync failed: {e.Message}");
            }
            finally
            {
                StopHeartbeatLoop();
                if (ReferenceEquals(socket, webSocket))
                    socket = null;
                webSocket?.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
                await Task.Delay(ReconnectDelay, cancellationToken);
        }
    }

    private void OnSocketOpened()
    {
        heartbeatAcknowledged = true;
        Debug.Log("Discord bot connected to the gateway.");
    }

    private void OnSocketMessage(DataFrame frame)
    {
        if (frame.Type != OpCode.Text || string.IsNullOrWhiteSpace(frame.Text))
            return;

        _ = HandleGatewayMessageAsync(frame.Text);
    }

    private void OnSocketError(Exception exception)
    {
        if (exception != null)
            Debug.LogWarning($"Discord bot gateway error: {exception.Message}");
    }

    private void OnSocketClosed(CloseStatusCode code, string reason)
    {
        Debug.Log($"Discord bot gateway closed: {(int)code} {reason}");
    }

    private async Task HandleGatewayMessageAsync(string json)
    {
        GatewayPayload payload;

        try
        {
            payload = JsonConvert.DeserializeObject<GatewayPayload>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot failed to parse gateway payload: {e.Message}");
            return;
        }

        if (payload == null)
            return;

        if (payload.s.HasValue)
            sequence = payload.s.Value;

        switch (payload.op)
        {
            case DispatchOpcode:
                await HandleDispatchAsync(payload.t, payload.d);
                break;
            case HeartbeatOpcode:
                await SendHeartbeatAsync();
                break;
            case ReconnectOpcode:
                await ReconnectAsync("Discord requested reconnect.");
                break;
            case InvalidSessionOpcode:
                sessionId = null;
                resumeGatewayUrl = null;
                await ReconnectAsync("Discord invalidated the session.");
                break;
            case HelloOpcode:
                heartbeatIntervalMs = payload.d?["heartbeat_interval"]?.Value<int>() ?? 45000;
                StartHeartbeatLoop();
                if (!string.IsNullOrWhiteSpace(sessionId) && sequence.HasValue)
                    await SendResumeAsync();
                else
                    await SendIdentifyAsync();
                break;
            case HeartbeatAckOpcode:
                heartbeatAcknowledged = true;
                break;
        }
    }

    private async Task HandleDispatchAsync(string eventType, JToken payload)
    {
        participation.HandleGuildEvent(eventType, payload);
        switch (eventType)
        {
            case "READY":
                sessionId = payload?["session_id"]?.Value<string>();
                resumeGatewayUrl = payload?["resume_gateway_url"]?.Value<string>();
                selfUserId = payload?["user"]?["id"]?.Value<string>();
                applicationId = payload?["application"]?["id"]?.Value<string>() ?? applicationId ?? selfUserId;
                Debug.Log("Discord bot session is ready.");
                await RegisterSlashCommandsAsync();
                break;
            case "MESSAGE_REACTION_ADD":
                HandleReactionEvent(payload, true);
                break;
            case "MESSAGE_REACTION_REMOVE":
                HandleReactionEvent(payload, false);
                break;
            case "INTERACTION_CREATE":
                await HandleInteractionAsync(payload);
                break;
            case "MESSAGE_CREATE":
                await HandleSubmissionMessageAsync(payload);
                break;
        }
    }

    private static void RebuildSubmissionRoutes(DiscordBotService configuring = null)
    {
        var neededMessageContent = allSubmissionChannels.Count > 0;
        var routes = new Dictionary<string, DiscordSubmissionChannelConfigs>();
        var conflicts = new HashSet<string>();
        foreach (var bot in knownBots)
        foreach (var pair in bot.submissionChannels)
        {
            if (routes.ContainsKey(pair.Key)) conflicts.Add(pair.Key);
            else routes.Add(pair.Key, pair.Value);
        }
        foreach (var channel in conflicts)
        {
            routes.Remove(channel);
            Debug.LogWarning($"Discord submissions channel {channel} has multiple routes; intake disabled until the configuration conflict is fixed.");
        }
        allSubmissionChannels = routes;
        if (neededMessageContent != (routes.Count > 0))
            foreach (var bot in knownBots)
                if (!ReferenceEquals(bot, configuring) && bot.enableBot && !string.IsNullOrWhiteSpace(bot.botToken))
                    bot.StartBot(); // Re-identify when a different show's routes change the required intents.
    }

    private bool IsSubmissionRouteCurrent(string channel, DiscordSubmissionChannelConfigs route) =>
        allSubmissionChannels.TryGetValue(channel, out var current) && ReferenceEquals(current, route);

    private async Task HandleSubmissionMessageAsync(JToken payload)
    {
        var channel = payload?.Value<string>("channel_id");
        if (string.IsNullOrWhiteSpace(channel) || !allSubmissionChannels.TryGetValue(channel, out var route)) return;
        var source = DiscordSubmissionMessage.Parse(payload);
        if (source == null || source.author == selfUserId) return;
        if (route.ImagesContextOnly) foreach (var image in source.images) image.ContextOnly = true;
        lock (submissionsLock) if (!submissionsInFlight.Add(source.Identity)) return;
        var revision = submissionsRevision;
        try
        {
            if (!ServerSource.GetChannelSnapshot().Any(target => string.Equals(target.slug, route.GeneratorSlug, StringComparison.OrdinalIgnoreCase))) return;
            // Cache signed CDN uploads promptly; vision/pitch work runs on Unity's main thread.
            await PrepareSubmissionImagesAsync(source);
            if (revision == submissionsRevision && IsSubmissionRouteCurrent(channel, route))
                QueueDiscordSubmission(source, route, channel, () => revision == submissionsRevision && IsSubmissionRouteCurrent(channel, route));
        }
        catch (Exception e) { Debug.LogWarning("Discord submission intake failed: " + e.Message); }
        finally { lock (submissionsLock) submissionsInFlight.Remove(source.Identity); }
    }

    private int GatewayIntents => IntentGuilds | IntentGuildMessageReactions |
        (allSubmissionChannels.Count == 0 ? 0 : IntentGuildMessages | IntentMessageContent);

    protected virtual Task PrepareSubmissionImagesAsync(WebSourceDocument source) => ImageAttachmentService.PrepareAsync(source.images, null);

    private bool QueueDiscordSubmission(WebSourceDocument source, DiscordSubmissionChannelConfigs route, string channel, Func<bool> isCurrent) =>
        ServerSource.QueueSubmission(route.GeneratorSlug, source, route, isCurrent,
            (candidate, store) => PublishSubmissionPitchAsync(candidate, store, string.IsNullOrWhiteSpace(route.PitchChannelId) ? channel : route.PitchChannelId));

    private DiscordSubmissionChannelConfigs CommandSubmissionRoute(string generatorSlug)
    {
        var routes = allSubmissionChannels.Values.Where(route => string.Equals(route.GeneratorSlug, generatorSlug, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (routes.Length == 0) return new DiscordSubmissionChannelConfigs { GeneratorSlug = generatorSlug };
        if (routes.Select(route => (route.PromptRoot, route.PitchChannelId, route.ImagesContextOnly, route.PitchExpirationMinutes,
            route.PitchMinimumVotesToQueue, route.PitchApprovalScore)).Distinct().Count() != 1)
            throw new ArgumentException("This generator has conflicting submission policies. Give its submission channels consistent review and voting settings before using `/idea`.");
        return routes[0];
    }

    private async Task PublishSubmissionPitchAsync(PitchCandidate candidate, PitchCandidateStore store, string channel)
    {
        var response = await SendRestJsonAsync($"https://discord.com/api/v10/channels/{channel}/messages", "POST", new
        {
            content = "# :new: From Discord!",
            embeds = new[] { PitchDiscordPublisher.BuildEmbed(candidate) },
            allowed_mentions = new { parse = Array.Empty<string>() }
        });
        var posted = response?.ToObject<DiscordPostedMessage>();
        if (string.IsNullOrWhiteSpace(posted?.id) || string.IsNullOrWhiteSpace(posted.channel_id))
            throw new InvalidDataException("Discord did not return a posted pitch message.");
        var previous = store.GetPostedPitchMessages(posted.channel_id, posted.id);
        candidate.discordBotApplicationId = applicationId;
        candidate.discordMessageId = posted.id;
        candidate.discordChannelId = posted.channel_id;
        candidate.status = PitchStatus.PostedForVote;
        store.Save(candidate);
        PinPitchMessage(posted, previous);
        AddDefaultPitchReactions(posted);
    }

    public static void DeleteBotPitchMessage(PitchCandidate candidate)
    {
        var bot = knownBots.FirstOrDefault(value => value.applicationId == candidate.discordBotApplicationId && !string.IsNullOrWhiteSpace(value.botToken));
        if (bot != null) _ = bot.DeleteBotPitchMessageAsync(candidate);
    }

    private async Task DeleteBotPitchMessageAsync(PitchCandidate candidate)
    {
        try { await SendRestRequestAsync($"https://discord.com/api/v10/channels/{candidate.discordChannelId}/messages/{candidate.discordMessageId}", "DELETE"); }
        catch (Exception e) { Debug.LogWarning("Discord expired submission poll deletion failed: " + e.Message); }
    }

    private void HandleReactionEvent(JToken payload, bool isAdd)
    {
        var userId = payload?["user_id"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(userId) || payload?["burst"]?.Value<bool>() == true ||
            payload?["type"]?.Value<int>() == 1)
            return;
        if (!string.IsNullOrWhiteSpace(selfUserId) && string.Equals(userId, selfUserId, StringComparison.Ordinal))
            return;

        var messageId = payload?["message_id"]?.Value<string>();
        var channelId = payload?["channel_id"]?.Value<string>();
        var emojiName = payload?["emoji"]?["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(channelId))
            return;

        var binding = FolderSource.FindReplayByDiscordMessage(messageId, channelId);
        var voteKind = GetVoteKind(emojiName);
        if (voteKind == ReplayVoteKind.None)
            return;

        var weight = participation.VoteWeight(payload?["guild_id"]?.Value<string>(), userId, payload?["member"]);
        var source = isAdd ? "discord-reaction-add" : "discord-reaction-remove";
        reactionVotes.Apply(channelId, messageId, userId, voteKind.ToString(), isAdd, weight, delta =>
        {
            var upDelta = voteKind == ReplayVoteKind.Up ? delta : 0;
            var downDelta = voteKind == ReplayVoteKind.Down ? delta : 0;
            if (binding != null)
            {
                var updated = FolderSource.ApplyVote(binding.channelKey, binding.slug, upDelta, downDelta, source, messageId);
                if (updated != null)
                    Debug.Log($"Discord replay vote updated: {binding.slug} -> up {updated.upVotes}, down {updated.downVotes}, score {updated.voteScore}");
                return updated != null;
            }
            return PitchCandidateStore.TryApplyVote(messageId, channelId, upDelta, downDelta, source);
        });
    }

    private ReplayVoteKind GetVoteKind(string emojiName)
    {
        if (string.Equals(emojiName, ThumbsUpEmoji, StringComparison.Ordinal) ||
            string.Equals(emojiName, "+1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(emojiName, "thumbsup", StringComparison.OrdinalIgnoreCase))
            return ReplayVoteKind.Up;

        if (string.Equals(emojiName, ThumbsDownEmoji, StringComparison.Ordinal) ||
            string.Equals(emojiName, "-1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(emojiName, "thumbsdown", StringComparison.OrdinalIgnoreCase))
            return ReplayVoteKind.Down;

        return ReplayVoteKind.None;
    }

    private async Task AddDefaultReplayReactionsAsync(DiscordPostedMessage message)
    {
        try
        {
            await AddReactionAsync(message.channel_id, message.id, ThumbsUpEmoji);
            await AddReactionAsync(message.channel_id, message.id, ThumbsDownEmoji);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot failed to add default replay reactions: {e.Message}");
        }
    }

    private async Task PinPitchMessageAsync(DiscordPostedMessage message, IEnumerable<DiscordMessageRef> previousPitchMessages)
    {
        try
        {
            await PinMessageAsync(message.channel_id, message.id);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot failed to pin pitch message: {e.Message}");
        }
    }

    private async Task PinNowPlayingMessageAsync(DiscordPostedMessage message)
    {
        try
        {
            await PinMessageAsync(message.channel_id, message.id);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot failed to pin now playing message: {e.Message}");
        }
    }

    private async Task UnpinNowPlayingMessageAsync(DiscordPostedMessage message)
    {
        try
        {
            await UnpinMessageAsync(message.channel_id, message.id);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot failed to unpin now playing message: {e.Message}");
        }
    }

    private async Task HandleInteractionAsync(JToken payload)
    {
        var type = payload?["type"]?.Value<int>() ?? 0;

        switch (type)
        {
            case InteractionPingType:
                await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new { content = "pong" });
                break;
            case ApplicationCommandAutocompleteType:
                await HandleAutocompleteAsync(payload);
                break;
            case ApplicationCommandType:
                await HandleApplicationCommandAsync(payload);
                break;
        }
    }

    private async Task HandleAutocompleteAsync(JToken payload)
    {
        var commandName = payload?["data"]?["name"]?.Value<string>();
        if (!string.Equals(commandName, "idea", StringComparison.OrdinalIgnoreCase))
            return;

        var focused = payload?["data"]?["options"]?.FirstOrDefault(option => option?["focused"]?.Value<bool>() == true);
        if (!string.Equals(focused?["name"]?.Value<string>(), "generator", StringComparison.OrdinalIgnoreCase))
            return;

        var query = focused?["value"]?.Value<string>() ?? string.Empty;
        var choices = ServerSource.GetChannelSnapshot()
            .Where(channel => channel.active)
            .Where(channel => string.IsNullOrWhiteSpace(query) ||
                channel.slug.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                channel.name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                channel.context.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(channel => new
            {
                name = string.IsNullOrWhiteSpace(channel.context) ? $"{channel.name} ({channel.slug})" : $"{channel.context}: {channel.name} ({channel.slug})",
                value = channel.slug
            })
            .ToArray();

        await RespondToInteractionAsync(payload, ApplicationCommandAutocompleteResultType, new { choices });
    }

    private async Task HandleApplicationCommandAsync(JToken payload)
    {
        var commandName = payload?["data"]?["name"]?.Value<string>();
        if (string.Equals(commandName, "idea", StringComparison.OrdinalIgnoreCase))
        {
            await HandleIdeaCommandAsync(payload);
            return;
        }

        await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
        {
            content = $"Unknown command `{commandName}`.",
            flags = 64
        });
    }

    private async Task HandleIdeaCommandAsync(JToken payload)
    {
        if (!enableIdeaCommand)
        {
            await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
            {
                content = "The `/idea` command is disabled right now.",
                flags = 64
            });
            return;
        }

        var generatorSlug = GetStringOption(payload, "generator");
        var prompt = GetStringOption(payload, "prompt");
        if (string.IsNullOrWhiteSpace(generatorSlug) || string.IsNullOrWhiteSpace(prompt))
        {
            await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
            {
                content = "Both `generator` and `prompt` are required.",
                flags = 64
            });
            return;
        }

        var userId = payload?["member"]?["user"]?["id"]?.Value<string>() ?? payload?["user"]?["id"]?.Value<string>();
        var limit = participation.DailyIdeaLimit(payload?["guild_id"]?.Value<string>(), userId, payload?["member"]);

        var target = ServerSource.GetChannelSnapshot()
            .FirstOrDefault(channel => string.Equals(channel.slug, generatorSlug, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            await RespondIdeaResultAsync(payload, $"Generator `{generatorSlug}` is not available right now.", false);
            return;
        }

        WebSourceDocument source;
        DiscordSubmissionChannelConfigs route;
        try
        {
            var idea = DiscordIdeaSubmission.FromInteraction(payload, prompt);
            source = DiscordSubmissionMessage.FromInteraction(payload, idea);
            route = CommandSubmissionRoute(target.slug);
            if (route.ImagesContextOnly) foreach (var image in source.images) image.ContextOnly = true;
        }
        catch (ArgumentException e)
        {
            await RespondIdeaResultAsync(payload, e.Message, false);
            return;
        }

        // Reject exhausted users before downloading, then reserve atomically after preparation.
        if (!TryConsumeIdeaQuota(userId, limit, out var used, out var resetAt, consume: false))
        {
            await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
            {
                content = $"You have used {used}/{limit} idea submissions today. Try again after {resetAt:yyyy-MM-dd HH:mm} local time.",
                flags = 64
            });
            return;
        }

        var revision = submissionsRevision;
        var inheritedPolicy = allSubmissionChannels.Values.Any(current => ReferenceEquals(current, route));
        var channel = payload.Value<string>("channel_id") ?? payload["channel"]?.Value<string>("id");
        if (string.IsNullOrWhiteSpace(route.PitchChannelId) && string.IsNullOrWhiteSpace(channel))
        {
            await RespondIdeaResultAsync(payload, "No channel is available for this submission's pitch vote.", false);
            return;
        }
        bool IsCurrent() => revision == submissionsRevision && enableIdeaCommand && route.Enabled &&
            (inheritedPolicy ? allSubmissionChannels.Values.Any(current => ReferenceEquals(current, route)) :
             !allSubmissionChannels.Values.Any(current => string.Equals(current.GeneratorSlug, target.slug, StringComparison.OrdinalIgnoreCase)));
        var deferred = source.images.Count > 0;
        if (deferred)
        {
            await RespondToInteractionAsync(payload, DeferredChannelMessageWithSourceResponseType, new { flags = 64 });
            try { await PrepareSubmissionImagesAsync(source); }
            catch (Exception)
            {
                await RespondIdeaResultAsync(payload, "The images could not be prepared. Nothing was queued; please try again.", true);
                return;
            }
            if (source.images.Any(image => string.IsNullOrEmpty(image.DataBase64)))
            {
                await RespondIdeaResultAsync(payload, "One or more images could not be downloaded or exceeded the image limits. Nothing was queued; please use PNG/JPEG images up to 2 MiB and try again.", true);
                return;
            }
        }

        if (!IsCurrent())
        {
            await RespondIdeaResultAsync(payload, "Submission settings changed while processing. Please try again.", deferred);
            return;
        }

        if (!TryConsumeIdeaQuota(userId, limit, out used, out resetAt))
        {
            await RespondIdeaResultAsync(payload, $"You have used {used}/{limit} idea submissions today. Try again after {resetAt:yyyy-MM-dd HH:mm} local time.", deferred);
            return;
        }

        if (!QueueDiscordSubmission(source, route, channel, IsCurrent))
        {
            RefundIdeaQuota(userId, resetAt);
            await RespondIdeaResultAsync(payload, $"Generator `{generatorSlug}` is not available right now.", deferred);
            return;
        }

        var imageNote = source.images.Count == 0 ? string.Empty : $" Included {source.images.Count} image(s).";
        await RespondIdeaResultAsync(payload, $"Submitted your idea for `{target.slug}` for pitch review. Approved pitches require a vote before generation.{imageNote} Usage: {used}/{limit} today.", deferred);
    }

    private Task RespondIdeaResultAsync(JToken payload, string content, bool deferred)
    {
        if (!deferred)
            return RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new { content, flags = 64 });
        var appId = payload?["application_id"]?.Value<string>() ?? applicationId;
        var interactionToken = payload?["token"]?.Value<string>();
        return SendRestRequestAsync($"https://discord.com/api/v10/webhooks/{appId}/{interactionToken}/messages/@original", "PATCH", new { content });
    }

    private void RefundIdeaQuota(string userId, DateTimeOffset resetAt)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        lock (usageLock)
            if (ideaUsage.TryGetValue(userId, out var usage) && usage.dayKey == resetAt.AddTicks(-1).ToString("yyyy-MM-dd") && usage.count > 0)
                usage.count--;
    }

    private bool TryConsumeIdeaQuota(string userId, int limit, out int usedAfter, out DateTimeOffset resetAt, bool consume = true)
    {
        var now = DateTimeOffset.Now;
        var dayKey = now.ToString("yyyy-MM-dd");
        resetAt = now.Date.AddDays(1);

        if (string.IsNullOrWhiteSpace(userId))
        {
            usedAfter = 1;
            return true;
        }

        lock (usageLock)
        {
            if (!ideaUsage.TryGetValue(userId, out var usage) || !string.Equals(usage.dayKey, dayKey, StringComparison.Ordinal))
                usage = new UserIdeaUsage { dayKey = dayKey, count = 0 };

            if (usage.count >= limit)
            {
                usedAfter = usage.count;
                return false;
            }

            if (consume)
            {
                usage.count++;
                ideaUsage[userId] = usage;
            }
            usedAfter = usage.count;
            return true;
        }
    }

    private static string GetStringOption(JToken payload, string optionName)
    {
        return payload?["data"]?["options"]?
            .FirstOrDefault(option => string.Equals(option?["name"]?.Value<string>(), optionName, StringComparison.OrdinalIgnoreCase))?["value"]?.Value<string>();
    }

    private async Task RegisterSlashCommandsAsync()
    {
        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(botToken))
            return;

        var commands = BuildSlashCommandsPayload();
        if (slashCommandGuildIds.Length == 0)
        {
            await PutJsonAsync($"https://discord.com/api/v10/applications/{applicationId}/commands", commands);
            return;
        }

        foreach (var guildId in slashCommandGuildIds)
            await PutJsonAsync($"https://discord.com/api/v10/applications/{applicationId}/guilds/{guildId}/commands", commands);
    }

    private object[] BuildSlashCommandsPayload()
    {
        var commands = new List<object>();
        if (enableIdeaCommand)
        {
            commands.Add(new
            {
                name = "idea",
                description = "Submit an idea for pitch review",
                type = 1,
                options = new object[]
                {
                    new
                    {
                        type = 3,
                        name = "generator",
                        description = "Which generator should receive the idea",
                        required = true,
                        autocomplete = true
                    },
                    new
                    {
                        type = 3,
                        name = "prompt",
                        description = "The idea or prompt to submit",
                        required = true,
                        max_length = 1000
                    }
                }.Concat(DiscordIdeaSubmission.ImageOptionNames.Select(name => (object)new
                {
                    type = 11,
                    name,
                    description = "Optional PNG/JPEG image context (up to 2 MiB)",
                    required = false
                })).Concat(new object[] { new
                {
                    type = 5,
                    name = "context_only",
                    description = "Keep all uploaded images off screen; use them only as idea context",
                    required = false
                } }).ToArray()
            });
        }

        return commands.ToArray();
    }

    private async Task RespondToInteractionAsync(JToken interaction, int responseType, object data)
    {
        var interactionId = interaction?["id"]?.Value<string>();
        var interactionToken = interaction?["token"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(interactionId) || string.IsNullOrWhiteSpace(interactionToken))
            return;

        await PostJsonAsync(
            $"https://discord.com/api/v10/interactions/{interactionId}/{interactionToken}/callback",
            new
            {
                type = responseType,
                data
            });
    }

    private void StartHeartbeatLoop()
    {
        StopHeartbeatLoop();
        heartbeatAcknowledged = true;
        heartbeatCts = new CancellationTokenSource();
        _ = RunHeartbeatLoopAsync(heartbeatCts.Token);
    }

    private void StopHeartbeatLoop()
    {
        heartbeatCts?.Cancel();
        heartbeatCts?.Dispose();
        heartbeatCts = null;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Mathf.Max(1000, heartbeatIntervalMs), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!heartbeatAcknowledged)
            {
                await ReconnectAsync("Discord heartbeat ack timed out.");
                break;
            }

            heartbeatAcknowledged = false;
            await SendHeartbeatAsync();
        }
    }

    private Task SendHeartbeatAsync()
    {
        return SendPayloadAsync(new GatewayOutgoingPayload
        {
            op = HeartbeatOpcode,
            d = sequence.HasValue ? JToken.FromObject(sequence.Value) : JValue.CreateNull()
        });
    }

    private Task SendIdentifyAsync()
    {
        return SendPayloadAsync(new GatewayOutgoingPayload
        {
            op = IdentifyOpcode,
            d = JToken.FromObject(new
            {
                token = botToken,
                intents = GatewayIntents,
                properties = new
                {
                    os = SystemInfo.operatingSystemFamily.ToString(),
                    browser = "hbox",
                    device = "hbox"
                }
            })
        });
    }

    private Task SendResumeAsync()
    {
        return SendPayloadAsync(new GatewayOutgoingPayload
        {
            op = ResumeOpcode,
            d = JToken.FromObject(new
            {
                token = botToken,
                session_id = sessionId,
                seq = sequence ?? 0
            })
        });
    }

    private async Task SendPayloadAsync(GatewayOutgoingPayload payload)
    {
        if (payload == null || socket == null || socket.State != State.Open)
            return;

        var json = JsonConvert.SerializeObject(payload);

        await sendLock.WaitAsync();
        try
        {
            if (socket != null && socket.State == State.Open)
                await socket.SendAsync(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot failed to send gateway payload: {e.Message}");
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task ReconnectAsync(string reason)
    {
        Debug.Log($"Discord bot reconnecting: {reason}");

        try
        {
            if (socket != null && socket.State == State.Open)
                await socket.CloseAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord bot reconnect close failed: {e.Message}");
        }
    }

    private Task AddReactionAsync(string channelId, string messageId, string emoji)
    {
        var encodedEmoji = Uri.EscapeDataString(emoji);
        return SendRestRequestAsync($"https://discord.com/api/v10/channels/{channelId}/messages/{messageId}/reactions/{encodedEmoji}/@me", "PUT");
    }

    private Task PinMessageAsync(string channelId, string messageId)
    {
        return SendRestRequestAsync($"https://discord.com/api/v10/channels/{channelId}/pins/{messageId}", "PUT");
    }

    private Task UnpinMessageAsync(string channelId, string messageId)
    {
        return SendRestRequestAsync($"https://discord.com/api/v10/channels/{channelId}/pins/{messageId}", "DELETE");
    }

    private Task PostJsonAsync(string url, object payload)
    {
        return SendRestRequestAsync(url, "POST", payload);
    }

    private Task PutJsonAsync(string url, object payload)
    {
        return SendRestRequestAsync(url, "PUT", payload);
    }

    protected virtual Task SendRestRequestAsync(string url, string method, object payload = null) => SendRestJsonAsync(url, method, payload);

    protected virtual async Task<JToken> SendRestJsonAsync(string url, string method, object payload = null)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.ContentType = "application/json";
            request.UserAgent = "HBOxDiscordBot/1.0";
            request.Headers[HttpRequestHeader.Authorization] = $"Bot {botToken}";

            if (payload != null)
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
                request.ContentLength = bytes.Length;
                using var stream = await request.GetRequestStreamAsync();
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            else
            {
                request.ContentLength = 0;
            }

            try
            {
                using var response = (HttpWebResponse)await request.GetResponseAsync();
                using var reader = new StreamReader(response.GetResponseStream());
                var text = await reader.ReadToEndAsync();
                return string.IsNullOrWhiteSpace(text) ? null : JToken.Parse(text);
            }
            catch (WebException e) when (DiscordRateLimit.TryGetRetryDelay(e, out var retryDelay) && attempt < maxAttempts)
            {
                Debug.LogWarning($"Discord REST rate-limited; retrying {method} in {retryDelay.TotalSeconds:0.##}s.");
                await Task.Delay(retryDelay);
            }
        }
        return null;
    }

    [Serializable]
    private sealed class GatewayPayload
    {
        public int op;
        public JToken d;
        public int? s;
        public string t;
    }

    [Serializable]
    private sealed class GatewayOutgoingPayload
    {
        public int op;
        public JToken d;
    }

    private sealed class UserIdeaUsage
    {
        public string dayKey;
        public int count;
    }

    private enum ReplayVoteKind
    {
        None = 0,
        Up = 1,
        Down = 2
    }
}
