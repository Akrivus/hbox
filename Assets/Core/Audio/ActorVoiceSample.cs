using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

// Immutable request snapshot; no references to mutable cast assets or credentials.
public sealed class ActorVoiceSample
{
    public const string StandardPhrase = "Good evening. Here is what we know, what remains uncertain, and why it matters. Really? Let's take a closer look.";
    public string ActorId { get; }
    public string Voice { get; }
    public string Model { get; }
    public string Language { get; }
    public string Instructions { get; }
    public string Phrase { get; }
    public string Id { get; }

    public ActorVoiceSample(Actor actor, string actorId, string phrase, string defaultGeminiModel)
    {
        if (actor == null || string.IsNullOrWhiteSpace(actorId))
            throw new ArgumentException("Select an existing actor asset.");
        if (string.IsNullOrWhiteSpace(actor.Voice)) throw new ArgumentException("Actor voice is missing.");
        if (string.IsNullOrWhiteSpace(phrase) || Encoding.UTF8.GetByteCount(phrase) > 500)
            throw new ArgumentException("Use a phrase of 1–500 UTF-8 bytes.");
        ActorId = actorId;
        Voice = actor.Voice;
        Language = string.IsNullOrWhiteSpace(actor.VoiceLanguage) ? "en-US" : actor.VoiceLanguage;
        Instructions = actor.VoiceInstructions ?? string.Empty;
        Phrase = phrase;
        Model = GoogleTtsClient.IsOpenAiVoice(Voice) ? "gpt-4o-mini-tts" :
            GoogleTtsClient.HasGeminiPrefix(Voice) ?
                (string.IsNullOrWhiteSpace(actor.VoiceModel) ? defaultGeminiModel : actor.VoiceModel) : "google-standard-tts";
        if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("TTS model is missing.");
        if (!GoogleTtsClient.IsOpenAiVoice(Voice))
            GoogleTtsClient.CreateRequest(Phrase, GoogleTtsClient.NormalizeGeminiVoice(Voice), Model,
                Instructions, Language, GoogleTtsClient.HasGeminiPrefix(Voice));
        using (var hash = SHA256.Create())
            Id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                new[] { "actor-audition-pcm16-v1", ActorId, Voice, Model, Language, Instructions, Phrase }))))
                .Replace("-", "").ToLowerInvariant();
    }

    public void ApplyTo(Actor temporaryActor)
    {
        temporaryActor.Voice = Voice;
        temporaryActor.VoiceModel = Model;
        temporaryActor.VoiceLanguage = Language;
        temporaryActor.VoiceInstructions = Instructions;
    }
}

// Cache operations never synthesize. A caller must explicitly authorize each cache miss.
public sealed class ActorVoiceSampleCache
{
    public sealed class Audio
    {
        public string Id;
        public string Pcm;
        public int Frequency;
    }
    private readonly string directory;
    private bool busy;
    public ActorVoiceSampleCache(string directory) { this.directory = directory; }
    private string PathFor(ActorVoiceSample sample) => Path.Combine(directory, sample.Id + ".json");

    public Audio Read(ActorVoiceSample sample)
    {
        var path = PathFor(sample);
        if (!File.Exists(path)) return null;
        try
        {
            var audio = JsonConvert.DeserializeObject<Audio>(File.ReadAllText(path));
            Validate(sample, audio);
            return audio;
        }
        catch (Exception error) when (error is JsonException || error is FormatException || error is ArgumentException)
        { return null; } // Corrupt cache is a miss, never an automatic paid retry.
    }

    public async Task<Audio> Generate(ActorVoiceSample sample, bool paidRequestConfirmed,
        Func<ActorVoiceSample, Task<Audio>> synthesize)
    {
        if (busy) throw new InvalidOperationException("An audition is already being synthesized.");
        var cached = Read(sample);
        if (cached != null) return cached;
        if (!paidRequestConfirmed) throw new InvalidOperationException("Confirm this paid synthesis request first.");
        busy = true;
        string temporary = null;
        try
        {
            var audio = await synthesize(sample);
            Validate(sample, audio);
            Directory.CreateDirectory(directory);
            temporary = PathFor(sample) + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(audio));
            if (File.Exists(PathFor(sample))) File.Delete(PathFor(sample));
            File.Move(temporary, PathFor(sample));
            return audio;
        }
        finally
        {
            busy = false;
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(ActorVoiceSample sample, Audio audio)
    {
        if (audio == null || audio.Id != sample.Id || (audio.Frequency != 24000 && audio.Frequency != 48000) ||
            string.IsNullOrEmpty(audio.Pcm)) throw new ArgumentException("No valid audition audio returned.");
        var bytes = Convert.FromBase64String(audio.Pcm);
        if (bytes.Length == 0 || bytes.Length % 2 != 0) throw new ArgumentException("Invalid PCM audio.");
    }
}
