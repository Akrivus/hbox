using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class MemoryBucket
{
    public static Dictionary<string, MemoryBucket> Buckets = new Dictionary<string, MemoryBucket>();

    public string Name { get; private set; }
    public List<Memory> Memories { get; private set; }

    public MemoryBucket(string name)
    {
        Name = name;
        Memories = new List<Memory>();
    }

    public async Task Add(string text)
    {
        Memories.Add(new Memory(text, Embed(text)));
        await Task.CompletedTask;
    }

    public async Task Save()
    {
        if (!Directory.Exists("./Memories"))
            Directory.CreateDirectory("./Memories");

        var json = JsonConvert.SerializeObject(Memories);
        await File.WriteAllTextAsync($"./Memories/{Name}.json", json);
        Buckets.Remove(Name);
    }

    public async Task Load()
    {
        if (!Directory.Exists("./Memories"))
            Directory.CreateDirectory("./Memories");
        if (!File.Exists($"./Memories/{Name}.json"))
            return;
        var json = await File.ReadAllTextAsync($"./Memories/{Name}.json");
        Memories = JsonConvert.DeserializeObject<List<Memory>>(json);
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

    private double[] Embed(string text)
    {
        return new double[0]; // LLM.EmbedAsync(text).Result;
    }

    public static async Task<MemoryBucket> Get(string name)
    {
        if (Buckets.ContainsKey(name))
            return Buckets[name];

        var bucket = new MemoryBucket(name);
        await bucket.Load();

        Buckets[name] = bucket;
        return bucket;
    }

    public static async Task<string> GetContext(string channel)
    {
        var bucket = await Get("#" + channel);
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
    public string Text { get; private set; }
    public double[] Embeddings { get; private set; }
    public DateTime Created { get; private set; }

    public Memory(string text, double[] embeddings)
    {
        Text = text;
        Embeddings = embeddings;
        Created = DateTime.Now;
    }
}