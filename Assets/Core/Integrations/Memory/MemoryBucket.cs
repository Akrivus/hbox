using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class MemoryBucket
{
    public static Dictionary<string, MemoryBucket> Buckets = new Dictionary<string, MemoryBucket>();

    public string Context { get; private set; }
    public string Name { get; private set; }
    public List<Memory> Memories { get; private set; }

    public MemoryBucket(string context, string name)
    {
        Context = context;
        Name = name;
        Memories = new List<Memory>();
    }

    public async Task Add(PromptResolver prompt)
    {
        await prompt.Nullable().Resolve();
        if (prompt.IsBlank)
            return;
        Memories.Add(new Memory(prompt, await Embed(prompt.Text)));
    }

    public async Task Save()
    {
        try
        {
            var folder = $"./Vault/{Context}/Memories";
            var path = $"{folder}/{Name}.json";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var json = JsonConvert.SerializeObject(Memories, Formatting.Indented);

            try
            {
                await File.WriteAllTextAsync(path, json);
                Buckets.Remove(Name);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to save memory bucket {Name}: {e.Message}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to serialize memory bucket {Name}: {e.Message}");
        }
    }

    public async Task Load()
    {
        try
        {
            var folder = $"./Vault/{Context}/Memories";
            var path = $"{folder}/{Name}.json";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            if (!File.Exists(path))
                return;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                Memories = JsonConvert.DeserializeObject<List<Memory>>(json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read memory bucket {Name}: {e.Message}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load memory bucket {Name}: {e.Message}");
        }
    }

    public async Task<string> Recall(string text)
    {
        var embeddings = await LLM.EmbedAsync(text);
        var memory = Memories.OrderBy(x => CosineSimilarity(x.Embeddings, embeddings)).First();
        return memory.Text;
    }

    public string Get(int length = 2048, bool exact = false)
    {
        var memory = Memories
            .OrderBy(x => x.Created)
            .Reverse()
            .Select(x => x.Text)
            .Where(s =>
            {
                if (length <= s.Length && exact)
                    return false;
                length -= s.Length;
                return length >= 0;
            }).ToArray();
        return string.Join("\n", memory);
    }

    public void Clean()
    {
        for (var i = 0; i < Memories.Count; i++)
        {
            var memory = Memories[i];
            var similar = Memories
                .Where(x => x != memory)
                .Where(x => CosineSimilarity(x.Embeddings, memory.Embeddings) > 0.9)
                .OrderBy(x => x.Created)
                .ToList();
            foreach (var s in similar)
                Memories.Remove(s);
        }
    }

    private async Task<double[]> Embed(string text)
    {
        return await LLM.EmbedAsync(text);
    }

    public static async Task<MemoryBucket> Get(ChatManagerContext context, string name)
    {
        if (Buckets.ContainsKey(name))
            return Buckets[name];

        var bucket = new MemoryBucket(context.Key, name);
        await bucket.Load();

        Buckets[name] = bucket;
        return bucket;
    }

    public static async Task<string> GetContext(ChatManagerContext context, string channel)
    {
        var bucket = await Get(context, "#" + channel);
        return bucket.Get();
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        var dotProduct = a.Zip(b, (x, y) => x * y).Sum();
        var magnitudeA = Math.Sqrt(a.Sum(x => x * x));
        var magnitudeB = Math.Sqrt(b.Sum(x => x * x));
        return dotProduct / (magnitudeA * magnitudeB);
    }
}

public class Memory
{
    [JsonIgnore]
    public PromptResolver Prompt => _prompt ??= new PromptResolver(ChatManager.Instance.Contexts[ContextKey], Path);
    public string ContextKey { get; private set; }
    public string Path { get; private set; }
    public double[] Embeddings { get; private set; }
    public DateTime Created { get; private set; }

    public string Text => Prompt == null ? string.Empty : Prompt.Text;

    private PromptResolver _prompt;

    public Memory(PromptResolver prompt, double[] embeddings)
    {
        _prompt = prompt;
        Path = prompt.Path;
        ContextKey = prompt.ManagerContext.Key;
        Embeddings = embeddings;
        Created = DateTime.Now;
    }
}