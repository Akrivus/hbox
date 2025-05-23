using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

public class PromptResolver
{
    public static string BasePromptPath = "Prompts";
    public static string BaseOutputPath = "Outputs";
    public static string BasePath = "Vault";

    public bool Resolved { get; private set; } = false;
    public string Part { get; private set; } = string.Empty;
    public string Path { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public PromptResolver Output { get; private set; } = null;

    public PromptResolver(Actor actor)
    {
        Part = "Actors/" + actor.Name;
        SetPromptPath();
    }

    public PromptResolver(ChatGenerator generator)
    {
        Part = generator.name;
        SetPromptPath();
        if (!File.Exists(Path))
            Part = "Defaults";
        SetPromptPath();
    }

    public PromptResolver(ChatGenerator generator, ISubGenerator sub)
    {

        Part = string.Join("/", generator.name, SplitTypeName(sub));
        SetPromptPath();
        if (!File.Exists(Path))
            Part = string.Join("/", "Defaults", SplitTypeName(sub));
        SetPromptPath();
    }

    public PromptResolver(params string[] path)
    {
        Part = string.Join("/", path);
        SetPromptPath(false);
    }

    public PromptResolver(bool direct, params string[] path)
    {
        Part = string.Join("/", path);
        SetPromptPath(direct);
    }

    public async Task<PromptResolver> Resolve(params object[] args)
    {
        if (!File.Exists(Path))
        {
            Debug.LogError($"Prompt file not found: {Path}");
            SetPromptPath();
            if (!File.Exists(Path))
                return this;
        }
        Text = await File.ReadAllTextAsync(Path);
        for (var i = 0; i < args.Length; ++i)
            if (args[i] != null)
                Text = Text.Replace("{" + i + "}", args[i].ToString());
        Resolved = true;
        return this;
    }

    public void Reset()
    {
        Resolved = false;
        Text = string.Empty;
        Path = string.Empty;
    }

    public async Task<Dictionary<string, string>> ExtractSet(string[] names, string context, string[] set = null)
    {
        if (set == null)
            set = names;
        var prompt = await Resolve(string.Join("\n- ", names), context);
        var message = await LLM.CompleteAsync(prompt, true);

        var lines = message.Parse(set);

        return lines
            .Where(line => set.Contains(line.Key))
            .ToDictionary(
                line => line.Key,
                line => line.Value);
    }

    public async Task SaveOutput(string text)
    {
        var folder = System.IO.Path.Combine(BasePath, ChatManager.Instance.name, BaseOutputPath, Part);
        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        var path = System.IO.Path.Combine(folder, timestamp + ".md");

        Output = new PromptResolver(true, path);
        await Save(path, text);
    }

    private async Task Save(string path, string text)
    {
        var folder = System.IO.Path.GetDirectoryName(path);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, text);
    }

    private void SetPromptPath(bool direct = false)
    {
        if (direct)
            Path = Part;
        else
            Path = System.IO.Path.Combine(BasePath, ChatManager.Instance.name, BasePromptPath, Part);
        if (!Path.EndsWith(".md")) Path += ".md";
    }

    private static string SplitTypeName(object type)
    {
        return Regex.Replace(type.GetType().Name, "(\\B[A-Z])", " $1");
    }
}