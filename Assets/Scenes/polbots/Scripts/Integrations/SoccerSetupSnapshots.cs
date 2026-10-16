using System;
using System.Linq;
using System.Reflection;
using FStudio.Database;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class SoccerSetupSnapshots
{
    // Clone() in the vendor shares kits and logos. Never rename/recolor those shared assets.
    public static TeamEntry Clone(TeamEntry source)
    {
        var team = (TeamEntry) source.Clone();
        team.name = source.name;
        team.HomeKit = UnityEngine.Object.Instantiate(source.HomeKit);
        team.AwayKit = UnityEngine.Object.Instantiate(source.AwayKit);
        team.TeamLogo = UnityEngine.Object.Instantiate(source.TeamLogo);
        return team;
    }

    public static SoccerTeamSnapshot Capture(TeamEntry team) => new SoccerTeamSnapshot
    {
        Template = team.name, Name = team.TeamName, Formation = (int)team.Formation,
        Players = team.Players.Select(CaptureFields).ToArray(),
        HomeKit = CaptureFields(team.HomeKit), AwayKit = CaptureFields(team.AwayKit), Logo = CaptureFields(team.TeamLogo)
    };

    public static TeamEntry Restore(SoccerTeamSnapshot snapshot, TeamEntry[] templates)
    {
        if (!Enum.IsDefined(typeof(FStudio.Data.Formations), snapshot.Formation))
            throw new FormatException("Unsupported formation.");
        var candidates = templates.Where(t => t != null && t.name == snapshot.Template).ToArray();
        if (candidates.Length != 1)
            throw new FormatException($"Expected one team template '{snapshot.Template}'.");
        var team = Clone(candidates[0]);
        try
        {
            if (snapshot.Players?.Length != team.Players.Length)
                throw new FormatException("Player slot count changed.");
            team.TeamName = snapshot.Name;
            team.Formation = (FStudio.Data.Formations)snapshot.Formation;
            for (var i = 0; i < team.Players.Length; i++) ApplyFields(snapshot.Players[i], team.Players[i]);
            ApplyFields(snapshot.HomeKit, team.HomeKit);
            ApplyFields(snapshot.AwayKit, team.AwayKit);
            ApplyFields(snapshot.Logo, team.TeamLogo);
            return team;
        }
        catch
        {
            Destroy(team); throw;
        }
    }

    public static void Destroy(TeamEntry team)
    {
        if (team == null) return;
        foreach (var player in team.Players)
            UnityEngine.Object.Destroy(player);
        UnityEngine.Object.Destroy(team.HomeKit);
        UnityEngine.Object.Destroy(team.AwayKit);
        UnityEngine.Object.Destroy(team.TeamLogo);
        UnityEngine.Object.Destroy(team);
    }

    private static FieldInfo[] Fields(object value) => value.GetType()
        .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(f => f.Name != "team").ToArray();

    private static JObject CaptureFields(object value)
    {
        var result = new JObject();
        foreach (var field in Fields(value))
        {
            var item = field.GetValue(value);
            // Textures remain external dependencies; refuse a different template on restore.
            result[field.Name] = typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                ? JToken.FromObject((item as UnityEngine.Object)?.name ?? "")
                : item is Color color ? JObject.FromObject(new { color.r, color.g, color.b, color.a })
                : item == null ? JValue.CreateNull() : JToken.FromObject(item);
        }
        return result;
    }

    private static void ApplyFields(JObject data, object target)
    {
        if (data == null)
            throw new FormatException("Missing team appearance snapshot.");
        foreach (var field in Fields(target))
        {
            if (!data.TryGetValue(field.Name, out var value))
                throw new FormatException($"Missing saved field '{field.Name}'.");
            if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
            {
                if (((field.GetValue(target) as UnityEngine.Object)?.name ?? "") != value.Value<string>())
                    throw new FormatException($"Template texture changed: {field.Name}.");
            }
            else
            {
                var converted = value.ToObject(field.FieldType);
                if (field.FieldType.IsEnum && !Enum.IsDefined(field.FieldType, converted))
                    throw new FormatException($"Unsupported {field.Name} value.");
                field.SetValue(target, converted);
            }
        }
    }
}
