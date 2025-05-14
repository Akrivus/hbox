using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class Chat
{
    public static string FolderName = "";
    public string FileName { get; set; }

    public string Title { get; set; }
    public string Synopsis { get; set; }

    public string Context { get; set; }
    public string Topic { get; set; } = "";
    public string Characters { get; set; } = "";

    public string Location { get; set; } = "";
    public ActorContext[] Actors { get; set; }
    public List<ChatNode> Nodes { get; set; }
    public Idea Idea { get; set; }

    public string Vibe { get; set; }

    public string TextureData { get; set; }
    public string[] Cues { get; set; }
    public string EndingTrigger { get; set; }

    [JsonIgnore]
    public Texture2D Texture
    {
        get => TextureData.ToTexture2D();
        set => TextureData = value.ToBase64();
    }


    [JsonIgnore]
    public bool IsLocked => _locked;

    private bool _locked;

    [JsonIgnore]
    public bool NewEpisode => _new;

    private bool _new;

    [JsonIgnore]
    public string Log => string.Join("\n", Nodes.Select(n => $"{n.Actor.Name}: {n.Text}"));

    [JsonIgnore]
    public string OriginalLog => string.Join("\n", Nodes.Select(n => $"{n.Actor.Name}: {n.OriginalText}"));

    [JsonIgnore]
    public string[] Names => Actors.Select(a => a.Name).ToArray();

    [JsonIgnore]
    public List<Message> Messages { get; set; }

    public Chat(Idea idea)
    {
        _new = true;
        FileName = idea.Slug;
        Idea = idea;
        Actors = new ActorContext[0];
        Nodes = new List<ChatNode>();
        _locked = false;
    }

    public Chat(string prompt) : this(new Idea(prompt))
    {

    }

    public Chat()
    {
        _locked = true;
    }

    public void Lock()
    {
        _locked = true;
    }

    public ChatNode NextNode => Nodes.FirstOrDefault(n => n.New);

    public async void Save()
    {
        if (!_locked) return;

        var json = JsonConvert.SerializeObject(this, Formatting.Indented);

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, FolderName);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        folder = Path.Combine(folder, $"{FileName}.json");

        await File.WriteAllTextAsync(folder, json);
    }

    public static string[] GetFiles()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, FolderName);
        return Directory.GetFiles(path, "*.json");
    }

    public static string[] GetNames()
    {
        return GetFiles().Select(Path.GetFileNameWithoutExtension).ToArray();
    }

    public static async Task<Chat> Load(string slug)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, FolderName, $"{slug}.json");
        var json = await File.ReadAllTextAsync(path);
        return JsonConvert.DeserializeObject<Chat>(json);
    }

    public static bool FileExists(string slug)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, FolderName, $"{slug}.json");
        return File.Exists(path);
    }
}
