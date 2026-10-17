using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public interface IConfig { string Type { get; } }
public class Sentiment { }
public class ChatManagerContext
{
    public static ChatManagerContext Current => throw new Exception("Auditions must not access a running channel");
    public string Name, Key;
}
// Minimal compile-only Unity surface; use the real Actor and sample implementation.
namespace UnityEngine
{
    public class ScriptableObject { }
    public class MonoBehaviour { }
    public class AudioClip { }
    public static class Debug { public static void Log(string text) { } public static void LogError(string text) { } }
    public class GameObject { }
    public struct Color { }
    public static class Random { public static int Range(int min, int max) => min; }
    public class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public class TextAreaAttribute : Attribute { }
}

class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); } catch (ArgumentException) { checks++; return; }
        catch (InvalidOperationException) { checks++; return; }
        throw new Exception(message);
    }
    static Task Reject(Action action, string message) => Reject(() => { action(); return Task.CompletedTask; }, message);
    static Actor Actor() => new Actor { Voice = "gemini:Kore", VoiceInstructions = "Measured and dry.", VoiceLanguage = "en-US" };
    static ActorVoiceSample Sample(Actor actor, string id = "actor-guid", string phrase = ActorVoiceSample.StandardPhrase,
        string model = "gemini-2.5-flash-tts") => new ActorVoiceSample(actor, id, phrase, model);
    static ActorVoiceSampleCache.Audio Audio(ActorVoiceSample sample) => new ActorVoiceSampleCache.Audio
        { Id = sample.Id, Pcm = "AAAAAA==", Frequency = 24000 };

    static async Task Main()
    {
        var actor = Actor();
        var sample = Sample(actor);
        await Reject(() => Sample(null), "Null actor accepted");
        await Reject(() => Sample(actor, ""), "Missing asset identity accepted");
        await Reject(() => Sample(actor, phrase: " "), "Blank phrase accepted");
        await Reject(() => Sample(actor, phrase: new string('é', 251)), "Oversize UTF-8 phrase accepted");
        actor.Voice = "";
        await Reject(() => Sample(actor), "Blank voice accepted");
        actor = Actor();
        Check(Sample(actor).Id == sample.Id, "Identity unstable");
        Check(Sample(actor, "different-actor").Id != sample.Id, "Actors share identity");
        Check(Sample(actor, phrase: "Different phrase.").Id != sample.Id, "Phrase ignored");
        Check(Sample(actor, model: "gemini-3.1-flash-tts-preview").Id != sample.Id, "Configured model ignored");
        actor.VoiceModel = "explicit-model";
        Check(Sample(actor).Model == "explicit-model", "Model override lost");
        Check(Sample(actor).Id == Sample(actor, model: "unused-default").Id, "Unused default changes identity");
        actor.VoiceModel = null;
        actor.VoiceLanguage = "fr-FR";
        Check(Sample(actor).Id != sample.Id, "Language ignored");
        actor.VoiceLanguage = null;
        Check(Sample(actor).Id == sample.Id, "Default language not normalized");
        actor.VoiceInstructions = "Excited";
        Check(Sample(actor).Id != sample.Id, "Instructions ignored");
        actor.Voice = "gemini:Charon";
        Check(Sample(actor).Id != sample.Id, "Voice ignored");
        // Snapshot is immutable even when original settings change; apply to a separate actor.
        var copy = new Actor(); sample.ApplyTo(copy);
        Check(copy.Voice == "gemini:Kore" && copy.VoiceInstructions == "Measured and dry." &&
            copy.VoiceLanguage == "en-US" && copy.VoiceModel == sample.Model, "Snapshot configuration lost");
        Check(actor.Voice == "gemini:Charon" && actor.VoiceInstructions == "Excited", "Original actor mutated");
        actor.Voice = "coral";
        Check(Sample(actor).Model == "gpt-4o-mini-tts", "OpenAI routing changed");
        actor.Voice = "en-US-Standard-D";
        Check(Sample(actor).Model == "google-standard-tts", "Standard routing changed");
        actor.Voice = "Kore";
        await Reject(() => Sample(actor), "Unprefixed Gemini silently accepted");

        var directory = Path.Combine(Path.GetTempPath(), "hbox-auditions-" + Guid.NewGuid());
        try
        {
            var cache = new ActorVoiceSampleCache(directory);
            var calls = 0;
            Func<ActorVoiceSample, Task<ActorVoiceSampleCache.Audio>> synth = request =>
                { calls++; return Task.FromResult(Audio(request)); };
            Check(cache.Read(sample) == null && calls == 0, "Read synthesized");
            await Reject(() => cache.Generate(sample, false, synth), "Unconfirmed request accepted");
            Check(calls == 0, "Unconfirmed request invoked provider");
            await cache.Generate(sample, true, synth);
            await cache.Generate(sample, false, synth);
            Check(calls == 1 && cache.Read(sample).Frequency == 24000, "Cache hit synthesized");
            Check(new ActorVoiceSampleCache(directory).Read(sample).Pcm == "AAAAAA==", "Cache did not persist");
            File.WriteAllText(Path.Combine(directory, sample.Id + ".json"), "{broken");
            Check(cache.Read(sample) == null, "Corrupt cache accepted");
            await Reject(() => cache.Generate(sample, false, synth), "Corrupt cache automatically regenerated");
            await Reject(() => cache.Generate(sample, true, _ => throw new InvalidOperationException("fake provider failure")), "Provider failure hidden");
            Check(cache.Read(sample) == null, "Failure cached");
            await Reject(() => cache.Generate(sample, true, _ => Task.FromResult<ActorVoiceSampleCache.Audio>(null)), "Empty response accepted");
            var bad = Audio(sample); bad.Id = "wrong";
            await Reject(() => cache.Generate(sample, true, _ => Task.FromResult(bad)), "Wrong sample cached");
            bad = Audio(sample); bad.Pcm = "AA==";
            await Reject(() => cache.Generate(sample, true, _ => Task.FromResult(bad)), "Odd PCM cached");
            bad = Audio(sample); bad.Frequency = 0;
            await Reject(() => cache.Generate(sample, true, _ => Task.FromResult(bad)), "Invalid rate cached");
            var pending = new TaskCompletionSource<ActorVoiceSampleCache.Audio>();
            var task = cache.Generate(sample, true, _ => pending.Task);
            await Reject(() => cache.Generate(sample, true, synth), "Concurrent generation accepted");
            pending.SetResult(Audio(sample)); await task;
            Check(cache.Read(sample) != null, "Failed request prevented later success");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        // Google production adapter performs exactly one synthesis HTTP request on transient failure.
        var handler = new FailureHandler();
        var client = new GoogleTtsClient(new HttpClient(handler));
        await Reject(async () => await client.Synthesize("Test.", "en-US-Standard-D", null, null, "en-US",
            new TTSConfigs { GoogleApiKey = "fake" }, false, 0), "HTTP failure hidden");
        Check(handler.Calls == 1, "Audition retried billable request");
        handler.NetworkFailure = true; handler.Calls = 0;
        try { await client.Synthesize("Test.", "en-US-Standard-D", null, null, "en-US",
            new TTSConfigs { GoogleApiKey = "fake" }, false, 0); throw new Exception("Network failure hidden"); }
        catch (HttpRequestException) { Check(handler.Calls == 1, "Network error retried"); }
        // Exercise the real production audition entry with an injected HTTP transport.
        var successHandler = new SuccessHandler();
        var google = new GoogleTtsClient(new HttpClient(successHandler));
        var settings = new TTSConfigs { GoogleApiKey = "fake", GoogleGeminiModel = "configured-model" };
        var node = new ChatNode { Actor = new Actor { Voice = "en-US-Standard-D", VoiceLanguage = "fr-FR" }, Say = "Test." };
        await TextToSpeechGenerator.GenerateAudition(node, settings, google);
        Check(node.AudioData == "AAAAAA==" && node.Frequency == 48000, "Production audio not forwarded");
        Check(successHandler.Language == "fr-FR" && successHandler.Voice == "en-US-Standard-D", "Production actor selection/language lost");
        await Reject(() => TextToSpeechGenerator.GenerateAudition(null, settings, google), "Missing actor accepted by production");
        await Reject(() => TextToSpeechGenerator.GenerateAudition(node, null, google), "Missing settings accepted");
        node.Say = new string('x', 501);
        await Reject(() => TextToSpeechGenerator.GenerateAudition(node, settings, google), "Long audition reached provider");
        Check(successHandler.Calls == 1, "Invalid request reached provider");
        node.Say = "Test.";
        node.Actor.Voice = "gemini:Charon";
        node.Actor.VoiceInstructions = "Dry wit";
        node.VoiceInstructions = "Quietly";
        // Fake ADC is only used by the mock OAuth/synthesis handler.
        var fakeCredentials = Path.Combine(Path.GetTempPath(), "audition-adc-" + Guid.NewGuid() + ".json");
        File.WriteAllText(fakeCredentials, "{\"type\":\"authorized_user\",\"client_id\":\"fake\",\"client_secret\":\"fake\",\"refresh_token\":\"fake\",\"quota_project_id\":\"fake\"}");
        try
        {
            settings.GoogleCredentialsPath = fakeCredentials;
            await TextToSpeechGenerator.GenerateAudition(node, settings, google);
            Check(successHandler.Voice == "Charon" && successHandler.Model == "configured-model" &&
                successHandler.Prompt == "Dry wit\nQuietly" && node.Frequency == 24000, "Production Gemini configuration lost");
            node.Actor.VoiceModel = "actor-model";
            await TextToSpeechGenerator.GenerateAudition(node, settings, google);
            Check(successHandler.Model == "actor-model", "Production actor model override lost");
            var failure = new FailureHandler();
            await Reject(() => TextToSpeechGenerator.GenerateAudition(node, settings,
                new GoogleTtsClient(new HttpClient(failure))), "Production failure hidden");
            Check(failure.Calls == 1, "Production audition retried synthesis");
        }
        finally { File.Delete(fakeCredentials); }
        Console.WriteLine($"Passed {checks} actor audition checks (no credentials or paid requests).");
    }
    sealed class SuccessHandler : HttpMessageHandler
    {
        public int Calls;
        public string Voice, Model, Language, Prompt;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fake\",\"expires_in\":3600}") };
            Calls++;
            var body = Newtonsoft.Json.Linq.JObject.Parse(await request.Content.ReadAsStringAsync());
            Voice = (string)body["voice"]["name"]; Model = (string)body["voice"]["modelName"];
            Language = (string)body["voice"]["languageCode"]; Prompt = (string)body["input"]["prompt"];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"audioContent\":\"AAAAAA==\"}") };
        }
    }
    sealed class FailureHandler : HttpMessageHandler
    {
        public int Calls;
        public bool NetworkFailure;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.Host == "oauth2.googleapis.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fake\",\"expires_in\":3600}") });
            Calls++;
            if (NetworkFailure) throw new HttpRequestException("fake network failure");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
