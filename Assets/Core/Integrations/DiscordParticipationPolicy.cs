using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

// Shared by reaction votes and /idea submissions. Guild metadata comes from
// the existing GUILDS intent; no privileged member intent or REST lookup is needed.
public sealed class DiscordParticipationPolicy
{
    private HashSet<string> adminRoles;
    private HashSet<string> adminUsers;
    private HashSet<string> boosterRoles;
    private readonly Dictionary<string, GuildRoles> guilds = new Dictionary<string, GuildRoles>(StringComparer.Ordinal);
    private readonly object sync = new object();
    private int regularLimit;
    private int boosterLimit;
    private int adminLimit;

    public DiscordParticipationPolicy(DiscordConfigs config)
    {
        Configure(config);
    }

    public void Configure(DiscordConfigs config)
    {
        lock (sync)
        {
            adminRoles = Ids(config?.AdminRoleIds);
            adminUsers = Ids(config?.AdminUserIds);
            boosterRoles = Ids(config?.BoosterRoleIds);
            regularLimit = Math.Max(1, config?.DefaultDailyIdeaLimit ?? 3);
            boosterLimit = Math.Max(regularLimit, config?.BoosterDailyIdeaLimit ?? 10);
            adminLimit = Math.Max(boosterLimit, config?.AdminDailyIdeaLimit ?? 30);
        }
    }

    public void HandleGuildEvent(string eventType, JToken payload)
    {
        lock (sync)
        {
            if (eventType == "READY") { guilds.Clear(); return; }
            var guildId = payload?["guild_id"]?.Value<string>() ?? payload?["id"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(guildId)) return;
            if (eventType == "GUILD_DELETE") { guilds.Remove(guildId); return; }
            if (eventType != "GUILD_CREATE" && eventType != "GUILD_UPDATE" &&
                eventType != "GUILD_ROLE_CREATE" && eventType != "GUILD_ROLE_UPDATE" && eventType != "GUILD_ROLE_DELETE") return;
            if (!guilds.TryGetValue(guildId, out var guild))
                guilds[guildId] = guild = new GuildRoles();
            if (payload["owner_id"] != null) guild.OwnerId = payload["owner_id"].Value<string>();
            if (payload["roles"] is JArray roles)
            {
                guild.Roles.Clear();
                foreach (var role in roles) UpdateRole(guild, role);
            }
            if (payload["role"] != null) UpdateRole(guild, payload["role"]);
            if (eventType == "GUILD_ROLE_DELETE") guild.Roles.Remove(payload["role_id"]?.Value<string>() ?? "");
        }
    }

    public int VoteWeight(string guildId, string userId, JToken member)
    {
        member = member as JObject;
        var roles = member?["roles"]?.Values<string>().ToArray() ?? Array.Empty<string>();
        lock (sync)
        {
            guilds.TryGetValue(guildId ?? "", out var guild);
            if ((!string.IsNullOrWhiteSpace(userId) && (adminUsers.Contains(userId) || userId == guild?.OwnerId)) ||
                HasAdministrator(member?["permissions"]) || roles.Any(adminRoles.Contains) ||
                (guild != null && (IsRole(guild, guildId, 3) || roles.Any(role => IsRole(guild, role, 3)))))
                return 3;
            if (roles.Any(boosterRoles.Contains) ||
                (member?["premium_since"] != null && member["premium_since"].Type != JTokenType.Null) ||
                (guild != null && roles.Any(role => IsRole(guild, role, 2))))
                return 2;
            return 1;
        }
    }

    public int DailyIdeaLimit(string guildId, string userId, JToken member)
    {
        var weight = VoteWeight(guildId, userId, member);
        return weight == 3 ? adminLimit : weight == 2 ? boosterLimit : regularLimit;
    }

    private static HashSet<string> Ids(string[] values) => new HashSet<string>(
        (values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()), StringComparer.Ordinal);

    private static bool HasAdministrator(JToken permissions) => ulong.TryParse(permissions?.Value<string>(),
        NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && (value & 8UL) != 0;

    private static bool IsRole(GuildRoles guild, string roleId, int weight) =>
        roleId != null && guild.Roles.TryGetValue(roleId, out var value) && value == weight;

    private static void UpdateRole(GuildRoles guild, JToken role)
    {
        var id = role?["id"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(id)) return;
        guild.Roles[id] = HasAdministrator(role["permissions"]) ? 3 :
            (role["tags"] as JObject)?.Property("premium_subscriber") != null ? 2 : 1;
    }

    private sealed class GuildRoles
    {
        public string OwnerId;
        public readonly Dictionary<string, int> Roles = new Dictionary<string, int>(StringComparer.Ordinal);
    }
}
