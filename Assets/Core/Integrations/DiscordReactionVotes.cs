using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// Keep the applied weight, since removal events have no member/role information.
public sealed class DiscordReactionVotes
{
    private readonly string path;
    private readonly Action<string> warn;
    private readonly object sync = new object();
    private readonly Dictionary<string, int> weights;

    public DiscordReactionVotes(string path, Action<string> warn)
    {
        this.path = path;
        this.warn = warn;
        try
        {
            weights = File.Exists(path) ? JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(path)) : null;
        }
        catch (Exception e) { warn?.Invoke($"Discord reaction weights could not be loaded: {e.Message}"); }
        weights = weights ?? new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public void Apply(string channelId, string messageId, string userId, string emoji, bool isAdd, int weight, Func<int, bool> apply)
    {
        var key = $"{channelId}/{messageId}/{userId}/{emoji}";
        lock (sync)
        {
            var known = weights.TryGetValue(key, out var previous);
            if ((isAdd && known && previous > 0) || (!isAdd && known && previous == 0)) return;
            // Untracked reactions predate weighting and contributed one vote.
            var delta = isAdd ? weight : -(known ? previous : 1);
            if (!apply(delta)) return;
            weights[key] = isAdd ? weight : 0;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(weights));
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            catch (Exception e) { warn?.Invoke($"Discord reaction weights could not be saved: {e.Message}"); }
        }
    }
}
