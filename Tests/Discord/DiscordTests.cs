using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

static class Tests
{
    private static int checks;
    static async Task Main()
    {
        SubmissionConfigCompatibility();
        var config = new DiscordConfigs { AdminUserIds = new[] { "admin" }, AdminRoleIds = new[] { "admin-role" }, BoosterRoleIds = new[] { "boost-role" } };
        var policy = new DiscordParticipationPolicy(config);
        Check(policy.VoteWeight("g", "u", null) == 1, "regular weight");
        Check(policy.VoteWeight("g", "admin", null) == 3, "configured user ID without member payload");
        Check(policy.VoteWeight("g", "u", Member("admin-role", "boost-role")) == 3, "admin wins over booster");
        Check(policy.VoteWeight("g", "u", Member("boost-role")) == 2, "configured booster role");
        Check(policy.VoteWeight("g", "u", JObject.Parse("{permissions:'8'}")) == 3, "interaction administrator permission");
        Check(policy.VoteWeight("g", "u", JObject.Parse("{permissions:'invalid', premium_since:null}")) == 1, "invalid permission / null boost");
        Check(policy.VoteWeight("g", "u", JObject.Parse("{premium_since:'2026-01-01'}")) == 2, "native boost membership");
        var guild = JObject.Parse("{id:'g', owner_id:'owner', roles:[{id:'native-admin',permissions:'8'},{id:'native-boost',permissions:'0',tags:{premium_subscriber:null}}]}");
        policy.HandleGuildEvent("GUILD_CREATE", guild);
        Check(policy.VoteWeight("g", "owner", null) == 3, "server owner");
        Check(policy.VoteWeight("g", "u", Member("native-admin")) == 3, "native admin role");
        Check(policy.VoteWeight("g", "u", Member("native-boost")) == 2, "native booster role null-valued tag");
        Check(policy.VoteWeight("other", "owner", Member("native-admin")) == 1, "guild isolation");
        policy.Configure(config);
        Check(policy.VoteWeight("g", "u", Member("native-admin")) == 3, "metadata survives reconfigure/resume");
        policy.HandleGuildEvent("GUILD_ROLE_UPDATE", JObject.Parse("{guild_id:'g',role:{id:'native-admin',permissions:'0'}}"));
        Check(policy.VoteWeight("g", "u", Member("native-admin")) == 1, "permission revoked");
        policy.HandleGuildEvent("GUILD_ROLE_CREATE", JObject.Parse("{guild_id:'g',role:{id:'new-admin',permissions:'8'}}"));
        Check(policy.VoteWeight("g", "u", Member("new-admin")) == 3, "role created");
        policy.HandleGuildEvent("GUILD_ROLE_DELETE", JObject.Parse("{guild_id:'g',role_id:'new-admin'}"));
        Check(policy.VoteWeight("g", "u", Member("new-admin")) == 1, "role deleted");
        Check(policy.DailyIdeaLimit("g", "admin", null) == 30 && policy.DailyIdeaLimit("g", "u", Member("boost-role")) == 10 && policy.DailyIdeaLimit("g", "u", null) == 3, "shared idea tiers");
        var clamped = new DiscordParticipationPolicy(new DiscordConfigs { DefaultDailyIdeaLimit = 12, BoosterDailyIdeaLimit = 2, AdminDailyIdeaLimit = 1, AdminUserIds = new[] { "admin" } });
        Check(clamped.DailyIdeaLimit("g", "admin", null) == 12, "quota tier clamping");
        policy.HandleGuildEvent("READY", null);
        Check(policy.VoteWeight("g", "owner", Member("native-boost")) == 1, "fresh sessions clear guild metadata");

        UnityEngine.Application.persistentDataPath = Path.Combine(Path.GetFullPath("Temp/DiscordTests"), Guid.NewGuid().ToString("N"));
        var bot = new DiscordBotService();
        bot.Configure(config);
        await Dispatch(bot, "GUILD_CREATE", guild);
        var admin = Reaction("replay", "u", "thumbsup", Member("native-admin"));
        await Dispatch(bot, "MESSAGE_REACTION_ADD", admin);
        Check(FolderSource.Counts.upVotes == 3, "replay native admin adds three");
        await Dispatch(bot, "MESSAGE_REACTION_ADD", admin);
        Check(FolderSource.Counts.upVotes == 3, "duplicate add ignored");
        // Restart with no cached roles and a removal event containing no member.
        bot = new DiscordBotService();
        bot.Configure(config);
        admin.Remove("member");
        await Dispatch(bot, "MESSAGE_REACTION_REMOVE", admin);
        Check(FolderSource.Counts.upVotes == 0, "restart removal reverses saved three");
        await Dispatch(bot, "MESSAGE_REACTION_REMOVE", admin);
        Check(FolderSource.Counts.upVotes == 0, "duplicate remove ignored");
        await Dispatch(bot, "MESSAGE_REACTION_ADD", Reaction("replay", "u", "thumbsup", Member("boost-role")));
        Check(FolderSource.Counts.upVotes == 2, "re-add uses changed role");
        await Dispatch(bot, "MESSAGE_REACTION_ADD", Reaction("pitch", "admin", "thumbsup", null));
        await Dispatch(bot, "MESSAGE_REACTION_ADD", Reaction("pitch", "regular", "thumbsup", null));
        await Dispatch(bot, "MESSAGE_REACTION_ADD", Reaction("pitch", "booster", "thumbsdown", Member("boost-role")));
        Check(PitchCandidateStore.Counts.upVotes == 4 && PitchCandidateStore.Counts.downVotes == 2 && PitchCandidateStore.Counts.voteScore == 2, "pitch routes weighted up/down deltas");
        await Dispatch(bot, "MESSAGE_REACTION_REMOVE", Reaction("pitch", "booster", "thumbsdown", null));
        Check(PitchCandidateStore.Counts.downVotes == 0, "downvote reversal");
        typeof(DiscordBotService).GetField("selfUserId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(bot, "self");
        await Dispatch(bot, "MESSAGE_REACTION_ADD", Reaction("pitch", "self", "thumbsup", null));
        var burst = Reaction("pitch", "admin", "thumbsdown", null); burst["burst"] = true;
        await Dispatch(bot, "MESSAGE_REACTION_ADD", burst);
        await Dispatch(bot, "MESSAGE_REACTION_ADD", Reaction("pitch", "admin", "other", null));
        Check(PitchCandidateStore.Counts.upVotes == 4 && PitchCandidateStore.Counts.downVotes == 0, "self seeds, bursts and unrelated emoji ignored");
        await Dispatch(bot, "MESSAGE_REACTION_REMOVE", Reaction("pitch", "legacy", "thumbsup", null));
        Check(PitchCandidateStore.Counts.upVotes == 3, "legacy removal subtracts one");

        var ledgerPath = Path.Combine(UnityEngine.Application.persistentDataPath, "ledger.json");
        var ledger = new DiscordReactionVotes(ledgerPath, message => throw new Exception(message));
        ledger.Apply("c", "m", "u", "Up", true, 3, delta => false);
        var total = 0;
        ledger.Apply("c", "m", "u", "Up", true, 3, delta => { total += delta; return true; });
        Check(total == 3, "unknown message does not consume reaction");
        await IdeaUploads();
        await SubmissionsIntake();
        await CommandSubmissionPolicy();
        Console.WriteLine($"{checks} Discord regression checks passed.");
    }

    static void SubmissionConfigCompatibility()
    {
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<DiscordConfigs>("{'TipsChannels':{'channel':{'GeneratorSlug':'show-default','PromptRoot':'Discord Tips'}}}");
        Check(legacy.SubmissionChannels["channel"].PromptRoot == "Discord Submissions", "legacy config key/default prompt root migrate to submissions");
        var saved = Newtonsoft.Json.JsonConvert.SerializeObject(legacy);
        Check(saved.Contains("SubmissionChannels") && !saved.Contains("TipsChannels"), "config serialization emits only submission terminology");
        foreach (var json in new[]
        {
            "{'TipsChannels':{'legacy':{'GeneratorSlug':'old'}},'SubmissionChannels':{}}",
            "{'SubmissionChannels':{},'TipsChannels':{'legacy':{'GeneratorSlug':'old'}}}"
        })
            Check(Newtonsoft.Json.JsonConvert.DeserializeObject<DiscordConfigs>(json).SubmissionChannels.Count == 0, "explicit canonical empty mapping overrides legacy config regardless of order");
        var custom = Newtonsoft.Json.JsonConvert.DeserializeObject<DiscordConfigs>("{'SubmissionChannels':{'channel':{'GeneratorSlug':'show-default','PromptRoot':'Custom Intake'}}}");
        Check(custom.SubmissionChannels["channel"].PromptRoot == "Custom Intake", "custom Vault roots remain untouched");
    }

    static JObject IdeaCommand(int images = 0)
    {
        var payload = JObject.Parse(@"{id:'interaction-1',application_id:'app',token:'test-token',type:2,guild_id:'guild',channel_id:'channel',member:{user:{id:'submitter',username:'Reporter'}},data:{name:'idea',options:[{name:'generator',type:3,value:'show-default'},{name:'prompt',type:3,value:'An idea with literal {0}'}],resolved:{attachments:{}}}}");
        for (var i = 0; i < images; i++)
        {
            var id = "asset-" + i;
            ((JArray)payload["data"]["options"]).Add(new JObject { ["name"] = DiscordIdeaSubmission.ImageOptionNames[i], ["type"] = 11, ["value"] = id });
            payload["data"]["resolved"]["attachments"][id] = new JObject { ["id"] = id, ["filename"] = "scene.jpg", ["size"] = 1024,
                ["content_type"] = "image/jpeg", ["url"] = "https://cdn.discordapp.com/attachments/channel/" + id + "/scene.jpg?ex=123&hm=signed", ["description"] = "A scene photo" };
        }
        return payload;
    }

    static async Task IdeaUploads()
    {
        var payload = IdeaCommand(4);
        var idea = DiscordIdeaSubmission.FromInteraction(payload, "Test");
        Check(idea.Images.Count == 4 && idea.Images[0].Id == "discord:attachment:asset-0" && idea.Images[3].Id.EndsWith("asset-3"), "selected upload order and IDs");
        Check(idea.Author == "submitter" && idea.Source == "discord" && idea.Slug == "discord:interaction-1", "Discord author and submission identity");
        Check(idea.Images[0].SourceUrl == "https://discord.com/channels/guild/channel" && idea.Images[0].SourceLabel.Contains("Reporter") && idea.Images[0].Caption.Contains("A scene photo"), "upload attribution and caption");
        Check(idea.Images.All(image => image.UseForGeneration && !image.PresentationEligible), "command uploads default to context only");
        var dm = IdeaCommand(1); dm["user"] = dm["member"]["user"]; dm.Remove("member"); dm.Remove("guild_id");
        Check(DiscordIdeaSubmission.FromInteraction(dm, "DM").Images[0].SourceUrl.Contains("/@me/channel"), "DM submission metadata");
        var duplicate = IdeaCommand(2); duplicate["data"]["options"][3]["value"] = "asset-0";
        Check(DiscordIdeaSubmission.FromInteraction(duplicate, "Duplicate").Images.Count == 1, "duplicate selected upload deduplicated");

        var config = new DiscordConfigs { DefaultDailyIdeaLimit = 1 };
        var bot = new UploadBot(); bot.Configure(config);
        var schema = JArray.FromObject(typeof(DiscordBotService).GetMethod("BuildSlashCommandsPayload", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bot, null));
        var options = (JArray)schema[0]["options"];
        Check(options.Count == 7 && options.Take(2).All(option => option.Value<bool>("required")) && options.Skip(2).Take(4).All(option => option.Value<int>("type") == 11 && !option.Value<bool>("required")) && options.Last.Value<string>("name") == "context_only", "slash command schema offers four attachment slots and a context-only veto");
        var contextOnly = IdeaCommand(1);
        ((JArray)contextOnly["data"]["options"]).Add(new JObject { ["name"] = "context_only", ["type"] = 5, ["value"] = true });
        Check(DiscordIdeaSubmission.FromInteraction(contextOnly, "Context").Images.Single().ContextOnly, "command context-only flag survives intake");

        ServerSource.Ideas.Clear(); ServerSource.Submissions.Clear();
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand(2));
        Check(ServerSource.Ideas.Count == 0 && ServerSource.Submissions.Count == 1 && ServerSource.Submissions[0].Source.images.Count == 2 && bot.Downloads == 2, "uploads enter one shared submission, never a direct idea");
        Check(bot.Requests.Count == 2 && bot.Requests[0].Body.Value<int>("type") == 5 && bot.Requests[0].Body["data"].Value<int>("flags") == 64 &&
            bot.Requests[1].Method == "PATCH" && bot.Requests[1].Url.EndsWith("/webhooks/app/test-token/messages/@original"), "ephemeral defer then edit original response");
        Check(bot.Requests[1].Body.Value<string>("content").Contains("Included 2") && bot.Requests[1].Body.Value<string>("content").Contains("Usage: 1/1"), "upload success includes image count and counts one submission");
        Check(ServerSource.Submissions[0].Source.images.All(image => image.DataBase64 != null && image.Sha256 != null && image.Analysis == null), "CDN bytes cached before shared pitch review");
        var roundTrip = Newtonsoft.Json.JsonConvert.DeserializeObject<WebSourceDocument>(Newtonsoft.Json.JsonConvert.SerializeObject(ServerSource.Submissions[0].Source));
        Check(roundTrip.images[0].DataBase64 == ServerSource.Submissions[0].Source.images[0].DataBase64 && roundTrip.author == "submitter", "uploaded bytes and author survive serialization");
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand(1));
        Check(bot.Downloads == 2 && ServerSource.Submissions.Count == 1 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("used 1/1"), "quota rejection happens before downloading");

        bot = new UploadBot(); bot.Configure(config); ServerSource.Ideas.Clear(); ServerSource.Submissions.Clear();
        var badMime = IdeaCommand(1); badMime["data"]["resolved"]["attachments"]["asset-0"]["content_type"] = "application/pdf";
        await Dispatch(bot, "INTERACTION_CREATE", badMime);
        Check(bot.Downloads == 0 && ServerSource.Submissions.Count == 0 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("PNG or JPEG"), "non-image uploads rejected before download");
        var big = IdeaCommand(1); big["data"]["resolved"]["attachments"]["asset-0"]["size"] = ImageAttachmentContext.MaxImageBytes + 1;
        await Dispatch(bot, "INTERACTION_CREATE", big);
        Check(bot.Downloads == 0 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("2 MiB"), "declared oversized upload rejected");
        var missing = IdeaCommand(1); ((JObject)missing["data"]["resolved"]["attachments"]).Remove("asset-0");
        await Dispatch(bot, "INTERACTION_CREATE", missing);
        Check(bot.Downloads == 0 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("attach it again"), "missing resolved upload rejected");
        var foreign = IdeaCommand(1); foreign["data"]["resolved"]["attachments"]["asset-0"]["url"] = "https://example.org/scene.jpg";
        await Dispatch(bot, "INTERACTION_CREATE", foreign);
        Check(bot.Downloads == 0 && ServerSource.Submissions.Count == 0, "upload requires Discord CDN URL");
        bot.FailDownload = true;
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand(2));
        Check(ServerSource.Submissions.Count == 0 && bot.Requests.Last().Body.Value<string>("content").Contains("Nothing was queued"), "invalid image bytes reject entire submission");
        bot.FailDownload = false;
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand());
        Check(ServerSource.Submissions.Count == 1 && ServerSource.Submissions[0].Source.images.Count == 0 && ServerSource.Submissions[0].Source.selftext.Contains("literal {0}") &&
            bot.Requests.Last().Body.Value<int>("type") == 4 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("Usage: 1/1"), "text-only path works; validation failures did not consume quota");

        bot = new UploadBot(); bot.Configure(config); ServerSource.Ideas.Clear(); ServerSource.Submissions.Clear(); ServerSource.AcceptSubmissions = false;
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand(1));
        Check(ServerSource.Submissions.Count == 0 && bot.Requests.Last().Body.Value<string>("content").Contains("not available"), "queue refusal completes deferred response");
        ServerSource.AcceptSubmissions = true;
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand());
        Check(ServerSource.Submissions.Count == 1 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("Usage: 1/1"), "queue refusal refunds reserved quota");
        bot = new UploadBot(); bot.Configure(new DiscordConfigs { EnableIdeaCommand = false });
        await Dispatch(bot, "INTERACTION_CREATE", IdeaCommand(1));
        Check(bot.Downloads == 0 && bot.Requests.Last().Body["data"].Value<string>("content").Contains("disabled"), "disabled idea command rejects uploads");

        bot = new UploadBot { HoldPreparation = new TaskCompletionSource<bool>() }; bot.Configure(config); ServerSource.Ideas.Clear(); ServerSource.Submissions.Clear();
        var second = IdeaCommand(1); second["id"] = "interaction-2";
        var firstSubmission = Dispatch(bot, "INTERACTION_CREATE", IdeaCommand(1));
        var secondSubmission = Dispatch(bot, "INTERACTION_CREATE", second);
        bot.HoldPreparation.SetResult(true);
        await Task.WhenAll(firstSubmission, secondSubmission);
        Check(ServerSource.Submissions.Count == 1 && bot.Requests.Count(request => request.Method == "PATCH" && request.Body.Value<string>("content").Contains("used 1/1")) == 1,
            "concurrent uploads cannot exceed daily quota after async preparation");
    }
    static JObject Submission(string id = "submission-1", string text = "A premise for any world") => new JObject
    {
        ["id"] = id, ["guild_id"] = "guild", ["channel_id"] = "submissions", ["type"] = 0,
        ["author"] = new JObject { ["id"] = "submitter", ["username"] = "Reporter" },
        ["content"] = text, ["attachments"] = new JArray()
    };

    static async Task SubmissionsIntake()
    {
        ServerSource.Submissions.Clear();
        var bot = new UploadBot();
        var route = new DiscordSubmissionChannelConfigs { GeneratorSlug = "show-default" };
        var config = new DiscordConfigs { DefaultDailyIdeaLimit = 0, SubmissionChannels = new Dictionary<string, DiscordSubmissionChannelConfigs> { ["submissions"] = route } };
        bot.Configure(config);
        var intents = (int)typeof(DiscordBotService).GetProperty("GatewayIntents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bot);
        Check((intents & (1 << 9)) != 0 && (intents & (1 << 15)) != 0, "submissions routes request guild messages and message content intents");
        var outside = Submission(); outside["channel_id"] = "outside";
        await Dispatch(bot, "MESSAGE_CREATE", outside);
        var robot = Submission(); robot["author"]["bot"] = true;
        await Dispatch(bot, "MESSAGE_CREATE", robot);
        var webhook = Submission(); webhook["webhook_id"] = "webhook";
        await Dispatch(bot, "MESSAGE_CREATE", webhook);
        var system = Submission(); system["type"] = 6;
        await Dispatch(bot, "MESSAGE_CREATE", system);
        await Dispatch(bot, "MESSAGE_CREATE", Submission(text: ""));
        Check(ServerSource.Submissions.Count == 0 && bot.SubmissionPreparations == 0, "outside channels, bots, webhooks, system and empty messages ignored");
        await Dispatch(bot, "MESSAGE_CREATE", Submission());
        Check(ServerSource.Submissions.Count == 1 && ServerSource.Submissions[0].Source.provider == "discord" && ServerSource.Submissions[0].Source.SourceUrl == "https://discord.com/channels/guild/submissions/submission-1", "submission preserves content and exact message provenance");
        Check(bot.Requests.Count == 0 && ServerSource.Submissions[0].Current(), "submission intake is quiet and independent of idea command quota");
        var imageOnly = Submission("submission-image", "");
        route.ImagesContextOnly = true;
        ((JArray)imageOnly["attachments"]).Add(IdeaCommand(1)["data"]["resolved"]["attachments"]["asset-0"]);
        await Dispatch(bot, "MESSAGE_CREATE", imageOnly);
        Check(ServerSource.Submissions.Count == 2 && ServerSource.Submissions.Last().Source.images.Single().DataBase64 != null && ServerSource.Submissions.Last().Source.images.Single().SourceUrl.EndsWith("/submission-image"), "image-only submission caches shared image asset before queue handoff");
        Check(ServerSource.Submissions.Last().Source.images.Single().ContextOnly, "submissions route can veto presentation without discarding contextual image bytes");
        route.ImagesContextOnly = false;
        var partial = Submission("submission-partial");
        var unsupported = (JObject)IdeaCommand(1)["data"]["resolved"]["attachments"]["asset-0"]; unsupported["content_type"] = "application/pdf";
        ((JArray)partial["attachments"]).Add(unsupported);
        var parsed = DiscordSubmissionMessage.Parse(partial);
        Check(parsed.images.Count == 0 && parsed.threadContext.Contains("unavailable") && parsed.selftext.Contains("premise"), "unsupported attachments preserve usable message with availability notes");
        bot.HoldSubmissionPreparation = new TaskCompletionSource<bool>();
        var pending = Dispatch(bot, "MESSAGE_CREATE", Submission("pending"));
        bot.Configure(new DiscordConfigs());
        bot.HoldSubmissionPreparation.SetResult(true); await pending;
        Check(ServerSource.Submissions.Count == 2 && !ServerSource.Submissions[0].Current(), "reload disabling route cancels queued and in-flight intake");
        intents = (int)typeof(DiscordBotService).GetProperty("GatewayIntents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bot);
        Check((intents & (1 << 15)) == 0, "unused message-content intent is not requested");
        route.Enabled = false; bot.Configure(config);
        await Dispatch(bot, "MESSAGE_CREATE", Submission("disabled"));
        Check(ServerSource.Submissions.Count == 2, "disabled route ignores new messages");
        try { new DiscordSubmissionChannelConfigs().Validate(); Check(false, "invalid route must fail"); }
        catch (ArgumentException) { checks++; }

        route.Enabled = true; route.PitchChannelId = "pbnn-polls"; bot.Configure(config);
        var listener = new UploadBot(); listener.Configure(new DiscordConfigs { ApplicationId = "submission-app", BotToken = "fake-token" });
        await Dispatch(listener, "MESSAGE_CREATE", Submission("shared-listener"));
        Check(ServerSource.Submissions.Count == 3, "enabled listener can ingest another show's configured route without a second bot session");
        var candidate = new PitchCandidate(); var store = new PitchCandidateStore();
        await ServerSource.Submissions.Last().Publish(candidate, store);
        Check(listener.Requests.Last().Url.EndsWith("/channels/pbnn-polls/messages") && listener.Requests.Last().Body["embeds"].Count() == 1 &&
            !listener.Requests.Last().Body["allowed_mentions"]["parse"].Any(), "submission poll posts directly to configured channel with mentions suppressed");
        Check(store.Saved == candidate && candidate.status == PitchStatus.PostedForVote && candidate.discordBotApplicationId == "submission-app" && candidate.discordMessageId == "submission-poll", "bot-posted poll is recorded in the same pitch gate");
        DiscordBotService.DeleteBotPitchMessage(candidate);
        Check(listener.Requests.Last().Method == "DELETE", "expired bot poll deletes through its original bot application");
        bot.Configure(new DiscordConfigs());
    }

    static async Task CommandSubmissionPolicy()
    {
        ServerSource.Submissions.Clear(); ServerSource.Ideas.Clear();
        var route = new DiscordSubmissionChannelConfigs { GeneratorSlug = "show-default", PitchChannelId = "pbnn-polls", PitchMinimumVotesToQueue = 3, PitchApprovalScore = 2, ImagesContextOnly = true };
        var owner = new UploadBot();
        owner.Configure(new DiscordConfigs { SubmissionChannels = new Dictionary<string, DiscordSubmissionChannelConfigs> { ["inbox"] = route } });
        var listener = new UploadBot(); listener.Configure(new DiscordConfigs { DefaultDailyIdeaLimit = 1 });
        await Dispatch(listener, "INTERACTION_CREATE", IdeaCommand(1));
        var admitted = ServerSource.Submissions.Single();
        Check(ServerSource.Ideas.Count == 0 && admitted.Source.id == "command:interaction-1" && admitted.Source.author == "submitter" && admitted.Source.SourceUrl == "https://discord.com/channels/guild/channel", "command creates attributed source material rather than a generation job");
        Check(ReferenceEquals(admitted.Settings, route) && admitted.Settings.PitchMinimumVotesToQueue == 3 && admitted.Source.images.Single().ContextOnly, "command inherits channel submission vote policy and image veto");
        Check(listener.Requests.Last().Body.Value<string>("content").Contains("pitch review") && listener.Requests.Last().Body.Value<string>("content").Contains("require a vote"), "command receipt describes review and voting instead of promising generation");
        await admitted.Publish(new PitchCandidate(), new PitchCandidateStore());
        Check(listener.Requests.Last().Url.EndsWith("/channels/pbnn-polls/messages"), "command uses the channel submission poll destination");
        owner.Configure(new DiscordConfigs());
        Check(!admitted.Current(), "removing inherited policy invalidates an already admitted command");

        ServerSource.Submissions.Clear();
        owner.Configure(new DiscordConfigs { SubmissionChannels = new Dictionary<string, DiscordSubmissionChannelConfigs> { ["inbox"] = route } });
        listener = new UploadBot { HoldPreparation = new TaskCompletionSource<bool>() }; listener.Configure(new DiscordConfigs { DefaultDailyIdeaLimit = 1 });
        var pending = Dispatch(listener, "INTERACTION_CREATE", IdeaCommand(1));
        owner.Configure(new DiscordConfigs()); listener.HoldPreparation.SetResult(true); await pending;
        Check(ServerSource.Submissions.Count == 0 && listener.Requests.Last().Body.Value<string>("content").Contains("settings changed"), "policy removal during upload cannot silently fall back to weaker defaults");
        await Dispatch(listener, "INTERACTION_CREATE", IdeaCommand());
        Check(ServerSource.Submissions.Count == 1 && listener.Requests.Last().Body["data"].Value<string>("content").Contains("Usage: 1/1"), "cancelled upload did not consume quota; next command uses standard gated submission defaults");

        ServerSource.Submissions.Clear();
        owner.Configure(new DiscordConfigs { SubmissionChannels = new Dictionary<string, DiscordSubmissionChannelConfigs>
        {
            ["inbox"] = route,
            ["other"] = new DiscordSubmissionChannelConfigs { GeneratorSlug = "show-default", PitchApprovalScore = 5 }
        } });
        listener = new UploadBot(); listener.Configure(new DiscordConfigs());
        await Dispatch(listener, "INTERACTION_CREATE", IdeaCommand(1));
        Check(ServerSource.Submissions.Count == 0 && listener.Requests.Last().Body["data"].Value<string>("content").Contains("conflicting submission policies"), "ambiguous generator policies reject commands instead of choosing a weaker gate");
        owner.Configure(new DiscordConfigs());
    }

    static JObject Member(params string[] roles) => new JObject { ["roles"] = new JArray(roles) };
    static JObject Reaction(string message, string user, string emoji, JToken member) => new JObject
    { ["guild_id"] = "g", ["channel_id"] = "c", ["message_id"] = message, ["user_id"] = user, ["emoji"] = new JObject { ["name"] = emoji }, ["member"] = member };
    static Task Dispatch(DiscordBotService bot, string type, JToken payload) => (Task)typeof(DiscordBotService)
        .GetMethod("HandleDispatchAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bot, new object[] { type, payload });
    static void Check(bool passed, string message) { if (!passed) throw new Exception(message); checks++; }
}

sealed class UploadBot : DiscordBotService
{
    public readonly List<(string Url, string Method, JObject Body)> Requests = new List<(string, string, JObject)>();
    public int Downloads;
    public bool FailDownload;
    public TaskCompletionSource<bool> HoldPreparation;
    public TaskCompletionSource<bool> HoldSubmissionPreparation;
    public int SubmissionPreparations;
    protected override Task<JToken> SendRestJsonAsync(string url, string method, object payload = null)
    {
        Requests.Add((url, method, payload == null ? new JObject() : JObject.FromObject(payload)));
        return Task.FromResult<JToken>(new JObject { ["id"] = "submission-poll", ["channel_id"] = "pbnn-polls" });
    }
    protected override async Task PrepareSubmissionImagesAsync(WebSourceDocument source)
    {
        SubmissionPreparations++;
        var command = source.id.StartsWith("command:");
        await ImageAttachmentService.PrepareAsync(source.images, null, _ =>
        {
            if (command)
            {
                if (Requests.Last().Body.Value<int>("type") != 5) throw new Exception("Download started before defer");
                Downloads++;
            }
            return Task.FromResult(FailDownload ? new byte[32] : File.ReadAllBytes("Assets/Scenes/PBNN/Fixtures/LocalImage.bytes"));
        });
        if (command && HoldPreparation != null) await HoldPreparation.Task;
        if (!command && HoldSubmissionPreparation != null) await HoldSubmissionPreparation.Task;
    }
    protected override Task SendRestRequestAsync(string url, string method, object payload = null)
    {
        Requests.Add((url, method, payload == null ? new JObject() : JObject.FromObject(payload)));
        return Task.CompletedTask;
    }
}
