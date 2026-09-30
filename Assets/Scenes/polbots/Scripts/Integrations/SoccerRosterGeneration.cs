using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

// Installed on polbots generators before dialogue. Other shows have no soccer policy.
public sealed class SoccerRosterGeneration : MonoBehaviour, ISubGenerator
{
    public bool IsBlocking => true;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        if (chat == null || chat.IsLocked || !SoccerGameSource.IsSoccerMode(chat))
            return chat;

        var homeName = SoccerGameSource.FindMetadata(chat.Topic, "Home");
        var awayName = SoccerGameSource.FindMetadata(chat.Topic, "Away");
        if (string.IsNullOrWhiteSpace(homeName)) homeName = SoccerGameSource.FindMetadata(chat.Idea?.Prompt, "Home");
        if (string.IsNullOrWhiteSpace(awayName)) awayName = SoccerGameSource.FindMetadata(chat.Idea?.Prompt, "Away");
        var home = chat.ManagerContext.ActorsSearch[homeName];
        var away = chat.ManagerContext.ActorsSearch[awayName];
        if (home == null || away == null || home == away)
            throw new InvalidOperationException("Soccer roster generation requires two distinct cast teams in Home/Away metadata.");

        var story = $"Original story / idea:\n{chat.Idea?.Prompt}\nScenario:\n{chat.Topic}\nScene context:\n{chat.Context}";
        var forbidden = chat.ManagerContext.ActorsSearch.List.Where(actor => actor != null)
            .SelectMany(actor => (actor.Aliases ?? Array.Empty<string>()).Append(actor.Name));
        // Football Simulator teams contain eleven slots; validate again against the actual team at playback.
        var names = await new SoccerRosterService().Generate(GetComponent<ChatGenerator>(), home.Name, away.Name,
            11, 11, story, forbidden);
        var roster = new SoccerPreparedRoster { Home = home.Name, Away = away.Name, Names = names, Story = story };
        chat.GeneratedData ??= new Dictionary<string, string>();
        chat.GeneratedData[SoccerPreparedRoster.Key] = JsonConvert.SerializeObject(roster);
        chat.Context += "\n\n" + roster.Context;
        return chat;
    }
}

public sealed class SoccerPreparedRoster
{
    public const string Key = "soccer.roster";
    public string Home { get; set; }
    public string Away { get; set; }
    public string[][] Names { get; set; }
    public string Story { get; set; }

    [JsonIgnore]
    public string Context => $"{Story}\n\nOn-field roster for {Home}: {string.Join(", ", Names[0])}\n" +
        $"On-field roster for {Away}: {string.Join(", ", Names[1])}\n" +
        "Roster figures are silent on-field players, never dialogue speakers. Only the listed personified countries/institutions speak and react to these figures. Historical and contemporary teammates are intentional. React to actual match events; do not invent player actions.";

    public static SoccerPreparedRoster Read(Chat chat)
    {
        if (chat?.GeneratedData == null || !chat.GeneratedData.TryGetValue(Key, out var json)) return null;
        return JsonConvert.DeserializeObject<SoccerPreparedRoster>(json)
            ?? throw new FormatException("Saved soccer roster is empty.");
    }
}
