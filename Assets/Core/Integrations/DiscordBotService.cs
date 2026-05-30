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

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly string ThumbsUpEmoji = char.ConvertFromUtf32(0x1F44D);
    private static readonly string ThumbsDownEmoji = char.ConvertFromUtf32(0x1F44E);

    private bool enableBot;
    private string botToken;
    private string gatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
    private string applicationId;
    private string[] slashCommandGuildIds = Array.Empty<string>();
    private bool enableIdeaCommand = true;
    private int defaultDailyIdeaLimit = 3;
    private int boosterDailyIdeaLimit = 10;
    private HashSet<string> boosterRoleIds = new HashSet<string>(StringComparer.Ordinal);

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
        defaultDailyIdeaLimit = Mathf.Max(1, config?.DefaultDailyIdeaLimit ?? 3);
        boosterDailyIdeaLimit = Mathf.Max(defaultDailyIdeaLimit, config?.BoosterDailyIdeaLimit ?? 10);
        boosterRoleIds = new HashSet<string>((config?.BoosterRoleIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);

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
        if (frame == null || frame.Type != OpCode.Text || string.IsNullOrWhiteSpace(frame.Text))
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
        }
    }

    private void HandleReactionEvent(JToken payload, bool isAdd)
    {
        var userId = payload?["user_id"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(selfUserId) && string.Equals(userId, selfUserId, StringComparison.Ordinal))
            return;

        var messageId = payload?["message_id"]?.Value<string>();
        var channelId = payload?["channel_id"]?.Value<string>();
        var emojiName = payload?["emoji"]?["name"]?.Value<string>();

        var binding = FolderSource.FindReplayByDiscordMessage(messageId, channelId);
        var voteKind = GetVoteKind(emojiName);
        if (voteKind == ReplayVoteKind.None)
            return;

        var upDelta = voteKind == ReplayVoteKind.Up ? (isAdd ? 1 : -1) : 0;
        var downDelta = voteKind == ReplayVoteKind.Down ? (isAdd ? 1 : -1) : 0;
        var source = isAdd ? "discord-reaction-add" : "discord-reaction-remove";

        if (binding != null)
        {
            var updated = FolderSource.ApplyVote(binding.channelKey, binding.slug, upDelta, downDelta, source, messageId);
            if (updated != null)
                Debug.Log($"Discord replay vote updated: {binding.slug} -> up {updated.upVotes}, down {updated.downVotes}, score {updated.voteScore}");
            return;
        }

        PitchCandidateStore.TryApplyVote(messageId, channelId, upDelta, downDelta, source);
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

        var memberRoles = payload?["member"]?["roles"]?.Values<string>()?.Where(role => !string.IsNullOrWhiteSpace(role)).ToArray() ?? Array.Empty<string>();
        var userId = payload?["member"]?["user"]?["id"]?.Value<string>() ?? payload?["user"]?["id"]?.Value<string>();
        var isBooster = memberRoles.Any(role => boosterRoleIds.Contains(role));
        var limit = isBooster ? boosterDailyIdeaLimit : defaultDailyIdeaLimit;

        if (!TryConsumeIdeaQuota(userId, limit, out var used, out var resetAt))
        {
            await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
            {
                content = $"You have used {used}/{limit} idea submissions today. Try again after {resetAt:yyyy-MM-dd HH:mm} local time.",
                flags = 64
            });
            return;
        }

        var target = ServerSource.GetChannelSnapshot()
            .FirstOrDefault(channel => string.Equals(channel.slug, generatorSlug, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
            {
                content = $"Generator `{generatorSlug}` was not found.",
                flags = 64
            });
            return;
        }

        if (!ServerSource.QueueIdea(generatorSlug, prompt))
        {
            await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
            {
                content = $"Generator `{generatorSlug}` is not available right now.",
                flags = 64
            });
            return;
        }

        await RespondToInteractionAsync(payload, ChannelMessageWithSourceResponseType, new
        {
            content = $"Queued your idea for `{target.slug}`. Usage: {used}/{limit} today.",
            flags = 64
        });
    }

    private bool TryConsumeIdeaQuota(string userId, int limit, out int usedAfter, out DateTimeOffset resetAt)
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

            usage.count++;
            ideaUsage[userId] = usage;
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
                description = "Queue an idea for a generator",
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
                        description = "The idea or prompt to queue",
                        required = true,
                        max_length = 1000
                    }
                }
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
                intents = IntentGuilds | IntentGuildMessageReactions,
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

    private async Task SendRestRequestAsync(string url, string method, object payload = null)
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
                return;
            }
            catch (WebException e) when (TryGetRetryDelay(e, out var retryDelay) && attempt < maxAttempts)
            {
                Debug.LogWarning($"Discord REST rate-limited; retrying {method} in {retryDelay.TotalSeconds:0.##}s.");
                await Task.Delay(retryDelay);
            }
        }
    }

    private static bool TryGetRetryDelay(WebException exception, out TimeSpan retryDelay)
    {
        retryDelay = TimeSpan.FromSeconds(1);
        if (exception?.Response is not HttpWebResponse response)
            return false;
        if ((int)response.StatusCode != 429)
            return false;

        if (double.TryParse(response.Headers["Retry-After"], NumberStyles.Float, CultureInfo.InvariantCulture, out var headerDelay))
        {
            retryDelay = TimeSpan.FromSeconds(Math.Max(0.25, headerDelay));
            return true;
        }

        try
        {
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            var body = reader.ReadToEnd();
            var retryAfter = JObject.Parse(body).Value<double?>("retry_after");
            if (retryAfter.HasValue)
                retryDelay = TimeSpan.FromSeconds(Math.Max(0.25, retryAfter.Value));
        }
        catch
        {
            retryDelay = TimeSpan.FromSeconds(1);
        }

        return true;
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
