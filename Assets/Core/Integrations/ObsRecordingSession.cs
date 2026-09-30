using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class RecordedEpisode
{
    public string channelKey;
    public string slug;
    public string title;
}

[Serializable]
public sealed class EpisodeRecording
{
    public string id = Guid.NewGuid().ToString("N");
    public string startedAt;
    public string stoppedAt;
    public string status;
    public string outputPath;
    public string error;
    public List<RecordedEpisode> episodes = new List<RecordedEpisode>();
}

/// <summary>One atomic journal per recording. Videos keep the exact OBS filename.</summary>
public sealed class RecordingCatalog
{
    public static readonly RecordingCatalog Default = new RecordingCatalog(Path.GetFullPath(Path.Combine("Recordings", "index")));
    private readonly string directory;
    private readonly object sync = new object();
    public RecordingCatalog(string directory) { this.directory = directory; }

    public void Save(EpisodeRecording recording)
    {
        lock (sync)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, recording.id + ".json");
            var temporary = path + ".tmp";
            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(recording, Formatting.Indented));
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    public List<EpisodeRecording> Find(string channelKey, string slug) => ReadAll()
        .Where(record => record.episodes.Any(e => string.Equals(e.channelKey, channelKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.slug, slug, StringComparison.OrdinalIgnoreCase))).ToList();

    public List<EpisodeRecording> ReadAll()
    {
        lock (sync)
        {
            var results = new List<EpisodeRecording>();
            if (!Directory.Exists(directory)) return results;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var record = JsonConvert.DeserializeObject<EpisodeRecording>(File.ReadAllText(path));
                if (record?.episodes != null) results.Add(record);
            }
            return results.OrderByDescending(r => r.startedAt).ToList();
        }
    }
}

/// <summary>Called serially by OBS; never associates files by directory order or NowPlaying.</summary>
public sealed class ObsRecordingSession
{
    private readonly RecordingCatalog catalog;
    private EpisodeRecording active;
    private Func<string, Task<JObject>> request;
    public ObsRecordingSession(RecordingCatalog catalog = null) { this.catalog = catalog ?? RecordingCatalog.Default; }

    public async Task BeginAsync(RecordedEpisode episode, bool split, Func<string, Task<JObject>> send)
    {
        if (string.IsNullOrWhiteSpace(episode?.channelKey) || string.IsNullOrWhiteSpace(episode.slug))
            throw new InvalidOperationException("Cannot record an episode without its channel key and slug.");
        if (active != null && (split || active.status != "recording")) await StopAsync();
        if (active != null)
        {
            var status = await request("GetRecordStatus");
            if (status.Value<bool>("outputActive") != true)
            {
                active.status = "unconfirmed";
                active.error = "OBS recording ended outside HBOx; its file cannot be confirmed.";
                catalog.Save(active);
                active = null;
                throw new IOException("OBS recording ended outside HBOx; its file cannot be confirmed.");
            }
            // Continuous mode is explicitly many-to-one, not a single-episode cut.
            active.episodes.Add(Copy(episode));
            catalog.Save(active);
            return;
        }

        var existing = await send("GetRecordStatus");
        if (existing.Value<bool>("outputActive"))
            throw new IOException("OBS is already recording outside this session. Stop it before starting episode recording.");
        var next = new EpisodeRecording
        {
            startedAt = DateTimeOffset.UtcNow.ToString("O"),
            status = "starting",
            episodes = new List<RecordedEpisode> { Copy(episode) }
        };
        catalog.Save(next); // Durable identity before starting any recording.
        active = next;
        request = send;
        try
        {
            await request("StartRecord");
            active.status = "recording";
            catalog.Save(active);
        }
        catch (Exception error)
        {
            active.status = "unconfirmed";
            active.error = error.Message;
            try { catalog.Save(active); }
            finally { active = null; }
            // Never guess whether a timed-out start succeeded or adopt a later recording.
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (active == null) return;
        // A failed catalog write after StopRecord retries persistence, not the OBS command.
        if (!string.IsNullOrEmpty(active.outputPath))
        {
            catalog.Save(active);
            active = null;
            return;
        }
        if (active.status == "unconfirmed")
        {
            catalog.Save(active);
            active = null;
            return;
        }
        JObject response;
        try { response = await request("StopRecord"); }
        catch (Exception error)
        {
            active.status = "unconfirmed";
            active.error = error.Message;
            try { catalog.Save(active); }
            finally { active = null; }
            throw;
        }
        active.outputPath = response.Value<string>("outputPath");
        active.stoppedAt = DateTimeOffset.UtcNow.ToString("O");
        active.status = string.IsNullOrWhiteSpace(active.outputPath) ? "unconfirmed" : "completed";
        active.error = active.status == "unconfirmed" ? "OBS did not return an outputPath." : null;
        catalog.Save(active);
        var errorMessage = active.error;
        active = null;
        if (errorMessage != null) throw new IOException(errorMessage);
    }

    private static RecordedEpisode Copy(RecordedEpisode episode) => new RecordedEpisode
    {
        channelKey = episode.channelKey, slug = episode.slug, title = episode.title
    };
}
