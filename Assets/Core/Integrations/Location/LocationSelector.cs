using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class LocationSelector : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private LocationDefinition[] _locations;

    private void Awake()
    {
        if (_locations.Length == 0)
            _locations = LocationManager.Instance.Locations;
    }

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var names = chat.Names;
        var topic = chat.Topic;

        var spawnPoints = await SelectSpawnPoints(prompt, chat, names, topic);
        foreach (var s in spawnPoints)
            chat.Actors.Get(s.Key.Reference).SpawnPoint = s.Value;

        var empty = spawnPoints
            .Where(s => string.IsNullOrEmpty(s.Value))
            .Select(s => s.Key)
            .ToArray();
        if (empty.Length == 0)
            return chat;

        var location = _locations.FirstOrDefault(l => l.name == chat.Location);
        if (location == null)
            location = _locations.First();
        var unused = location.SpawnPoints
            .Where(s => !spawnPoints.Values.Contains(s.name))
            .ToArray();
        for (var i = 0; i < empty.Length; i++)
            empty[i].SpawnPoint = unused[i].name;

        return chat;
    }

    private async Task<Dictionary<ActorContext, string>> SelectSpawnPoints(PromptResolver prompt, Chat chat, string[] names, string topic)
    {
        var options = string.Join("\n\n", _locations
            .Where(s => s.SpawnPoints.Length >= names.Length)
            .Select(s => s.Line)
            .ToArray());
        var characters = string.Join(", ", names);
        var message = await LLM.CompleteAsync(await prompt.Resolve(options, characters, topic), chat, true);

        var lines = message.Parse(names.Concat(new[] { "Location" }).ToArray());
        var location = lines.TryGetValue("Location", out var l) ? l : "Default";
        chat.Location = location;

        return lines
            .Where(line => names.Contains(line.Key))
            .ToDictionary(
                line => chat.Actors.Get(line.Key),
                line => line.Value);
    }
}
