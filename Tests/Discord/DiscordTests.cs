using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

static class Tests
{
    private static int checks;
    static async Task Main()
    {
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
        Console.WriteLine($"{checks} Discord regression checks passed.");
    }
    static JObject Member(params string[] roles) => new JObject { ["roles"] = new JArray(roles) };
    static JObject Reaction(string message, string user, string emoji, JToken member) => new JObject
    { ["guild_id"] = "g", ["channel_id"] = "c", ["message_id"] = message, ["user_id"] = user, ["emoji"] = new JObject { ["name"] = emoji }, ["member"] = member };
    static Task Dispatch(DiscordBotService bot, string type, JToken payload) => (Task)typeof(DiscordBotService)
        .GetMethod("HandleDispatchAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(bot, new object[] { type, payload });
    static void Check(bool passed, string message) { if (!passed) throw new Exception(message); checks++; }
}
