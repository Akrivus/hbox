using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class MemoryBucket
{
    private const int MaxLoadedBuckets = 24;
    private const int DefaultContextLength = 2048;

    public static Dictionary<string, MemoryBucket> Buckets = new Dictionary<string, MemoryBucket>();

    public string Context { get; private set; }
    public string Name { get; private set; }
    public List<Memory> Memories { get; private set; }
    public bool IsDirty { get; private set; }
    public DateTime LastAccessedUtc { get; private set; }

    public MemoryBucket(string context, string name)
    {
        Context = context;
        Name = name;
        Memories = new List<Memory>();
        Touch();
    }

    public async Task Add(PromptResolver prompt)
    {
        await prompt.Nullable().Resolve();
        if (prompt.IsBlank)
            return;
        Memories.Add(new Memory(prompt, await Embed(prompt.Text)));
        IsDirty = true;
        Touch();
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
                IsDirty = false;
                Touch();
                Buckets.Remove(GetBucketKey(Context, Name));
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
                Memories = JsonConvert.DeserializeObject<List<Memory>>(json) ?? new List<Memory>();
                foreach (var memory in Memories)
                    if (memory != null)
                        await memory.ResolvePrompt();
                Touch();
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
        Touch();
        var memories = await GetRankedMemories(text);
        return memories.FirstOrDefault()?.Text ?? string.Empty;
    }

    public async Task<string> GetRelevant(string query, int length = DefaultContextLength, bool exact = false)
    {
        Touch();

        if (!LLM.USE_EMBEDDINGS || string.IsNullOrWhiteSpace(query))
            return Get(length, exact);

        var ranked = await GetRankedMemories(query);
        if (ranked.Count == 0)
            return Get(length, exact);

        return JoinMemories(ranked.Select(x => x.Text), length, exact);
    }

    public string Get(int length = DefaultContextLength, bool exact = false)
    {
        Touch();
        var texts = Memories
            .OrderBy(x => x.Created)
            .Reverse()
            .Select(x => x.Text);
        return JoinMemories(texts, length, exact);
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

    private async Task<List<Memory>> GetRankedMemories(string query)
    {
        if (Memories == null || Memories.Count == 0)
            return new List<Memory>();

        var queryEmbeddings = await LLM.EmbedAsync(query);
        if (!HasEmbeddings(queryEmbeddings))
            return new List<Memory>();

        await EnsureEmbeddings(queryEmbeddings.Length);

        return Memories
            .Where(x => HasEmbeddings(x.Embeddings))
            .Select(x => new
            {
                Memory = x,
                Similarity = CosineSimilarity(x.Embeddings, queryEmbeddings)
            })
            .Where(x => !double.IsNaN(x.Similarity) && !double.IsInfinity(x.Similarity))
            .OrderByDescending(x => x.Similarity)
            .ThenByDescending(x => x.Memory.Created)
            .Select(x => x.Memory)
            .ToList();
    }

    private async Task EnsureEmbeddings(int dimensions)
    {
        if (!LLM.USE_EMBEDDINGS)
            return;

        foreach (var memory in Memories.Where(x => x != null && NeedsEmbeddingRefresh(x, dimensions) && !string.IsNullOrWhiteSpace(x.Text)))
        {
            memory.SetEmbeddings(await Embed(memory.Text));
            if (HasEmbeddings(memory.Embeddings))
                IsDirty = true;
        }
    }

    private static string JoinMemories(IEnumerable<string> texts, int length, bool exact)
    {
        var remaining = length;
        var memory = texts
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s =>
            {
                if (remaining <= s.Length && exact)
                    return false;
                remaining -= s.Length;
                return remaining >= 0;
            })
            .ToArray();
        return string.Join("\n", memory);
    }

    private static bool HasEmbeddings(double[] embeddings)
    {
        return embeddings != null && embeddings.Length > 0;
    }

    private static bool NeedsEmbeddingRefresh(Memory memory, int dimensions)
    {
        return !HasEmbeddings(memory.Embeddings) || memory.Embeddings.Length != dimensions;
    }

    public static async Task<MemoryBucket> Get(ChatManagerContext context, string name)
    {
        var key = GetBucketKey(context.Key, name);
        if (Buckets.ContainsKey(key))
        {
            Buckets[key].Touch();
            return Buckets[key];
        }

        var bucket = new MemoryBucket(context.Key, name);
        await bucket.Load();

        Buckets[key] = bucket;
        await EvictIfNeeded(bucket);
        return bucket;
    }

    public static async Task<string> GetContext(ChatManagerContext context, string channel)
    {
        return await GetContext(context, channel, null);
    }

    public static async Task<string> GetContext(ChatManagerContext context, string channel, string query)
    {
        var bucket = await Get(context, "#" + channel);
        return await bucket.GetRelevant(query);
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        if (!HasEmbeddings(a) || !HasEmbeddings(b) || a.Length != b.Length)
            return double.NaN;

        var dotProduct = a.Zip(b, (x, y) => x * y).Sum();
        var magnitudeA = Math.Sqrt(a.Sum(x => x * x));
        var magnitudeB = Math.Sqrt(b.Sum(x => x * x));
        if (magnitudeA <= 0 || magnitudeB <= 0)
            return double.NaN;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    private static string GetBucketKey(string context, string name)
    {
        return $"{context}:{name}";
    }

    private static async Task EvictIfNeeded(MemoryBucket keepAlive = null)
    {
        while (Buckets.Count > MaxLoadedBuckets)
        {
            var evictionCandidate = Buckets.Values
                .Where(bucket => bucket != null && bucket != keepAlive)
                .OrderBy(bucket => bucket.LastAccessedUtc)
                .FirstOrDefault();

            if (evictionCandidate == null)
                return;

            var key = GetBucketKey(evictionCandidate.Context, evictionCandidate.Name);
            if (evictionCandidate.IsDirty)
            {
                await evictionCandidate.Save();
                continue;
            }

            Buckets.Remove(key);
        }
    }

    private void Touch()
    {
        LastAccessedUtc = DateTime.UtcNow;
    }
}

public class Memory
{
    [JsonIgnore]
    public PromptResolver Prompt
    {
        get
        {
            if (_prompt != null)
                return _prompt;

            if (!TryResolveContext(out var context))
                return null;

            _prompt = PromptResolver.FromPath(context, Path);
            return _prompt;
        }
    }
    public string ContextKey { get; private set; }
    public string Path { get; private set; }
    public double[] Embeddings { get; private set; }
    public DateTime Created { get; private set; }

    public string Text => Prompt == null ? string.Empty : Prompt.Text;

    private PromptResolver _prompt;

    private Memory()
    {
    }

    public Memory(PromptResolver prompt, double[] embeddings)
    {
        _prompt = prompt;
        Path = prompt.Path;
        ContextKey = prompt.ManagerContext.Key;
        Embeddings = embeddings;
        Created = DateTime.Now;
    }

    public async Task ResolvePrompt()
    {
        if (_prompt != null || string.IsNullOrWhiteSpace(Path))
            return;
        if (!TryResolveContext(out var context))
            return;

        _prompt = await PromptResolver.ResolveFromPath(context, Path);
    }

    public void SetEmbeddings(double[] embeddings)
    {
        Embeddings = embeddings;
    }

    private bool TryResolveContext(out ChatManagerContext context)
    {
        context = null;
        return !string.IsNullOrWhiteSpace(ContextKey) &&
            ChatManager.Instance != null &&
            ChatManager.Instance.Contexts.TryGetValue(ContextKey, out context) &&
            context != null;
    }
}
