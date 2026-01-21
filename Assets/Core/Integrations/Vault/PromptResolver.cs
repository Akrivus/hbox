using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

public class PromptResolver
{
    public static string EngineName = null;
    public static string BasePromptPath = "Prompts";
    public static string BaseInputPath = "Inputs";
    public static string BaseOutputPath = "Outputs";
    public static string BasePath = "Vault";

    public bool IsBlank => Resolved && string.IsNullOrEmpty(Text);

    public bool Resolved { get; private set; } = false;
    public string Part { get; private set; } = string.Empty;
    public string Path { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public PromptResolver Output { get; private set; } = null;
    public ChatManagerContext ManagerContext { get; private set; }
    public string FolderName { get; private set; }

    private bool nullable = false;

    public PromptResolver(Actor actor)
    {
        Part = "Actors/" + actor.Name;
        ManagerContext = actor.ManagerContext;
        FolderName = ManagerContext.Name;
        SetPromptPath(ManagerContext.Key);
    }

    public PromptResolver(ChatGenerator generator)
    {
        Part = generator.name;
        ManagerContext = generator.ManagerContext;
        FolderName = ManagerContext.Name;
        SetPromptPath(ManagerContext.Key);
        if (!File.Exists(Path))
            Part = "Defaults";
        SetPromptPath(ManagerContext.Key);
    }

    public PromptResolver(ChatGenerator generator, ISubGenerator sub)
    {

        Part = string.Join("/", generator.name, SplitTypeName(sub));
        ManagerContext = generator.ManagerContext;
        FolderName = ManagerContext.Name;
        SetPromptPath(ManagerContext.Key);
        if (!File.Exists(Path))
            Part = string.Join("/", "Defaults", SplitTypeName(sub));
        SetPromptPath(ManagerContext.Key);
    }

    public PromptResolver(ChatManagerContext chatManagerContext, params string[] path)
    {
        Part = string.Join("/", path);
        ManagerContext = chatManagerContext;
        FolderName = ManagerContext.Name;
        SetPromptPath(ManagerContext.Key);
    }

    public PromptResolver Nullable()
    {
        nullable = true;
        return this;
    }

    public async Task<PromptResolver> Resolve(params object[] args)
    {
        if (!File.Exists(Path))
        {
            if (!nullable) throw new Exception($"Prompt '{Path}' not found.");
            Text = string.Empty;
            Resolved = true;
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

    public async Task<Dictionary<string, string>> ExtractSet(Chat chat, string[] names, string context, string[] set = null)
    {
        if (set == null)
            set = names;
        var prompt = await Resolve(string.Join("\n- ", names), context);
        var message = await LLM.CompleteAsync(prompt, chat, true);

        var lines = message.Parse(set);

        return lines
            .Where(line => set.Contains(line.Key))
            .ToDictionary(
                line => line.Key,
                line => line.Value);
    }

    public async Task SaveInput()
    {
        var folder = System.IO.Path.Combine(BasePath, FolderName, BaseInputPath, Part);
        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        var path = System.IO.Path.Combine(folder, timestamp + ".md");
        await Save(path, Text);
    }

    public async Task SaveOutput(string text)
    {
        var folder = System.IO.Path.Combine(BasePath, FolderName, BaseOutputPath, Part);
        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        var path = System.IO.Path.Combine(folder, timestamp + ".md");

        Output = new PromptResolver(ManagerContext, path);
        await Save(path, text);
    }

    private async Task Save(string path, string text)
    {
        var folder = System.IO.Path.GetDirectoryName(path);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(path, text);
    }

    private void SetPromptPath(string name, bool direct = false)
    {
        if (direct)
            Path = Part;
        else
            Path = System.IO.Path.Combine(BasePath, name, BasePromptPath, Part);
        if (!Path.EndsWith(".md")) Path += ".md";
    }

    private static string SplitTypeName(object type)
    {
        return Regex.Replace(type.GetType().Name, "(\\B[A-Z])", " $1");
    }

    public static PromptResolver Find(ChatManagerContext context, string part)
    {
        var resolver = new PromptResolver(context, part);
        if (File.Exists(resolver.Path))
            return resolver;
        return null;
    }

    public static bool TryFind(ChatManagerContext context, string part, out PromptResolver resolver)
    {
        resolver = Find(context, part);
        return resolver != null;
    }

    public static async Task<string> Read(ChatManagerContext context, string path, string blank = null)
    {
        if (TryFind(context, path, out var resolver))
        {
            await resolver.Resolve();
            return resolver.Text;
        }
        return blank;
    }
}