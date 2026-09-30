using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class SoccerRosterService
{
    public async Task<string[][]> Generate(ChatGenerator generator, string home, string away,
        int homeCount, int awayCount, string story, IEnumerable<string> forbiddenNames)
    {
        var prompt = new PromptResolver(generator.ManagerContext, "Soccer Mode", "Roster Generation");
        await prompt.Resolve(home, away, homeCount.ToString(), awayCount.ToString(), story);
        var content = await LLM.CompleteAsync(prompt, null);
        return Parse(content, homeCount, awayCount, forbiddenNames);
    }

    public static string[][] Parse(string content, int homeCount, int awayCount, IEnumerable<string> forbiddenNames)
    {
        var json = JObject.Parse((content ?? string.Empty).Trim().Replace("```json", "").Replace("```", ""));
        var used = new HashSet<string>(forbiddenNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var result = new string[2][];
        var keys = new[] { "home", "away" };
        var counts = new[] { homeCount, awayCount };
        for (var side = 0; side < keys.Length; side++)
        {
            if (!(json[keys[side]] is JArray names) || names.Count != counts[side])
                throw new FormatException("Roster must fill every simulator player slot.");
            result[side] = names.Select(token => token.Type == JTokenType.String ? token.Value<string>()?.Trim() : null).ToArray();
            foreach (var name in result[side])
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(new[] { '\r', '\n', ':' }) >= 0 || !used.Add(name))
                    throw new FormatException("Roster contains an invalid, duplicate, or speaking cast name.");
            }
        }
        return result;
    }

    public static string[] Fallback(string country, IEnumerable<string> names, int count)
    {
        var result = (names ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(count).ToList();
        for (var number = 1; result.Count < count; number++)
        {
            var name = $"{country} Player {number}";
            if (!result.Contains(name)) result.Add(name);
        }
        return result.ToArray();
    }
}
