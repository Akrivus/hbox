using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public interface IConfig { string Type { get; } }

class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static void Throws(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { checks++; return; }
        catch (InvalidOperationException) { checks++; return; }
        throw new Exception(message);
    }
    static byte[] Wav(byte[] pcm, int rate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(40 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEJUNK")); writer.Write(3); writer.Write(new byte[4]);
        writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
        return stream.ToArray();
    }
    static async Task Main(string[] args)
    {
        if (args.Contains("--live-root-credentials"))
        {
            var liveSettings = new TTSConfigs { GoogleCredentialsPath = Path.GetFullPath("google-credentials.json") };
            var liveApi = new GoogleTtsClient(new HttpClient(new LiveDiagnosticHandler()));
            try
            {
                var audio = await liveApi.Synthesize("Audio check.", "Kore", liveSettings.GoogleGeminiModel, null, "en-US", liveSettings, true);
                Console.WriteLine($"Live Gemini TTS succeeded: {audio.Length} PCM bytes at 24000 Hz. Credentials and tokens were not logged.");
            }
            catch (Exception error) { Console.WriteLine(error.Message); Environment.ExitCode = 1; }
            return;
        }
        var settings = new TTSConfigs();
        var existingSettings = Newtonsoft.Json.JsonConvert.DeserializeObject<TTSConfigs>("{\"Type\":\"tts\",\"ReplaceOpenAiVoicesWithGoogle\":true}");
        Check(existingSettings.GoogleGeminiModel == settings.GoogleGeminiModel, "Old migration config should be ignored.");
        foreach (var voice in new[] { "echo", "coral", "ash", "alloy", "ballad", "fable", "onyx", "nova", "sage", "shimmer", "verse" })
            Check(GoogleTtsClient.IsOpenAiVoice(voice) && !GoogleTtsClient.HasGeminiPrefix(voice), "Existing OpenAI voice routing changed.");
        Check(!GoogleTtsClient.IsOpenAiVoice("en-US-Standard-D") && !GoogleTtsClient.HasGeminiPrefix("en-US-Standard-D"), "Existing Standard routing changed.");
        Check(GoogleTtsClient.HasGeminiPrefix("gemini:Kore"), "Gemini prefix not recognized.");
        Check(GoogleTtsClient.NormalizeGeminiVoice("gemini:Kore") == "Kore", "Gemini prefix sent to API.");
        Check(GoogleTtsClient.NormalizeGeminiVoice("Kore") == "Kore", "Direct Gemini helper voice changed.");
        Check(!GoogleTtsClient.HasGeminiPrefix("Kore"), "Unprefixed actor silently switched to Gemini.");
        var request = GoogleTtsClient.CreateRequest("Hello", "Charon", "gemini-2.5-flash-tts", "Dry delivery", "en-US", true);
        Check((string)request["input"]["prompt"] == "Dry delivery" && (string)request["voice"]["modelName"] == "gemini-2.5-flash-tts", "Gemini controls missing.");
        Check((string)request["audioConfig"]["audioEncoding"] == "LINEAR16" && request["audioConfig"]["pitch"] == null, "Invalid Gemini audio controls.");
        Throws(() => GoogleTtsClient.CreateRequest("Hello", "en-US-Standard-D", settings.GoogleGeminiModel, null, "en-US", true), "Standard voice accepted as Gemini.");
        Throws(() => GoogleTtsClient.CreateRequest("Hello", "Kore", null, null, "en-US", false), "Google helper silently switched to Gemini.");
        request = GoogleTtsClient.CreateRequest("Hello", "en-US-Standard-D", null, "Ignored", "en-US", false);
        Check(request["input"]["prompt"] == null && request["voice"]["modelName"] == null && (string)request["audioConfig"]["audioEncoding"] == "LINEAR16", "Legacy request changed.");
        Check((int)request["audioConfig"]["sampleRateHertz"] == 48000 && (double)request["audioConfig"]["speakingRate"] == 1.1 &&
            (int)request["audioConfig"]["pitch"] == 1 && (int)request["audioConfig"]["volumeGainDb"] == 1,
            "Existing Standard voice settings changed.");
        Throws(() => GoogleTtsClient.CreateRequest("Hello", "Kore", settings.GoogleGeminiModel, new string('é', 2001), "en-US", true), "Oversized direction accepted.");
        foreach (var text in new[] { new string('a', 4001), string.Concat(Enumerable.Repeat("😀你好 word. ", 1000)), "", "Short line" })
        {
            var chunks = GoogleTtsClient.SplitText(text).ToArray();
            Check(string.Concat(chunks) == text, "Chunking lost text.");
            Check(chunks.All(chunk => Encoding.UTF8.GetByteCount(chunk) <= 4000), "Oversized chunk.");
            Check(chunks.All(chunk => !char.IsHighSurrogate(chunk.Last()) && !char.IsLowSurrogate(chunk.First())), "Split surrogate pair.");
        }
        var pcm = new byte[] { 0, 0, 255, 127 };
        Check(GoogleTtsClient.DecodeAudio(pcm, 24000).SequenceEqual(pcm), "Raw PCM changed.");
        Check(GoogleTtsClient.DecodeAudio(Wav(pcm, 48000), 48000).SequenceEqual(pcm), "WAV chunk/header decoding failed.");
        Throws(() => GoogleTtsClient.DecodeAudio(Wav(pcm, 24000), 48000), "Wrong WAV rate accepted.");
        Throws(() => GoogleTtsClient.DecodeAudio(new byte[1], 24000), "Odd PCM accepted.");
        Throws(() => GoogleTtsClient.DecodeAudio(Wav(pcm, 48000).Take(30).ToArray(), 48000), "Truncated WAV accepted.");

        var credentials = Path.Combine("Temp", "TtsTests", "fake-adc.json");
        Directory.CreateDirectory(Path.GetDirectoryName(credentials));
        File.WriteAllText(credentials, "{\"type\":\"authorized_user\",\"client_id\":\"fake-client\",\"client_secret\":\"fake-secret\",\"refresh_token\":\"fake-refresh\",\"quota_project_id\":\"fake-project\"}");
        try
        {
            settings.GoogleCredentialsPath = Path.GetFullPath(credentials);
            var handler = new Handler(pcm);
            var api = new GoogleTtsClient(new HttpClient(handler));
            var bytes = await api.Synthesize(new string('a', 4001), "Charon", settings.GoogleGeminiModel, "Dry delivery", "en-US", settings, true);
            Check(bytes.SequenceEqual(pcm.Concat(pcm)), "Chunk concatenation failed.");
            Check(handler.Refreshes == 1 && handler.Requests == 3, "OAuth cache or transient retry failed.");
            Check(handler.Texts[0].Length == 4000 && handler.Texts[1] == handler.Texts[2] && handler.Texts[2].Length == 1, "Successful first chunk retried.");
            handler.Status = HttpStatusCode.Forbidden;
            var before = handler.Requests;
            try { await api.Synthesize("No", "Charon", settings.GoogleGeminiModel, null, "en-US", settings, true); throw new Exception("403 accepted."); }
            catch (InvalidOperationException) { Check(handler.Requests == before + 1, "403 retried."); }
            handler.Status = HttpStatusCode.OK;
            settings.GoogleApiKey = "fake-key";
            await api.Synthesize("Legacy", "en-US-Standard-D", null, null, "en-US", settings, false);
            Check(handler.LegacyRequests == 1 && handler.Refreshes == 1, "Legacy auth changed.");

            using var rsa = RSA.Create(2048);
            var encodedKey = rsa.ExportPkcs8PrivateKey();
            foreach (var length in new[] { 0, 1, 16, encodedKey.Length - 1 })
            {
                try
                {
                    GoogleTtsClient.ReadPkcs8RsaParameters(encodedKey.Take(length).ToArray());
                    throw new Exception("Truncated service-account key accepted.");
                }
                catch (CryptographicException) { checks++; }
            }
            var wrongAlgorithm = (byte[])encodedKey.Clone();
            var rsaOid = new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 };
            var oidOffset = Enumerable.Range(0, wrongAlgorithm.Length - rsaOid.Length)
                .First(i => wrongAlgorithm.Skip(i).Take(rsaOid.Length).SequenceEqual(rsaOid));
            wrongAlgorithm[oidOffset + 8] = 2;
            try
            {
                GoogleTtsClient.ReadPkcs8RsaParameters(wrongAlgorithm);
                throw new Exception("Non-RSA service-account key accepted.");
            }
            catch (CryptographicException) { checks++; }
            File.WriteAllText(credentials, new JObject
            {
                ["type"] = "service_account", ["client_email"] = "test@example.iam.gserviceaccount.com",
                ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(), ["project_id"] = "fake-project"
            }.ToString());
            handler = new Handler(pcm) { ServiceAccountKey = rsa };
            api = new GoogleTtsClient(new HttpClient(handler));
            await api.Synthesize("Service account", "Charon", settings.GoogleGeminiModel, null, "en-US", settings, true);
            settings.GoogleApiKey = null;
            await api.Synthesize("Legacy OAuth", "en-US-Standard-D", null, null, "en-US", settings, false);
            Check(handler.Refreshes == 1 && handler.LegacyRequests == 1, "Service account cache or legacy OAuth failed.");
            Check(GoogleTtsClient.ResolveCredentialsPath(settings) == Path.GetFullPath(credentials), "Explicit credentials path changed.");
            var previousEnvironment = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
            try
            {
                settings.GoogleCredentialsPath = null;
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentials);
                Check(GoogleTtsClient.ResolveCredentialsPath(settings) == Path.GetFullPath(credentials), "Environment credentials not resolved.");
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", null);
                if (File.Exists("google-credentials.json"))
                    Check(GoogleTtsClient.ResolveCredentialsPath(settings) == Path.GetFullPath("google-credentials.json"), "Project-root credentials not discovered.");
            }
            finally { Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", previousEnvironment); }
        }
        finally { File.Delete(credentials); }
        Console.WriteLine($"Passed {checks} Google TTS checks (mock HTTP; no billable calls).");
    }

    sealed class Handler : HttpMessageHandler
    {
        readonly byte[] pcm;
        public int Refreshes, Requests, LegacyRequests;
        public List<string> Texts = new List<string>();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public RSA ServiceAccountKey;
        public Handler(byte[] pcm) { this.pcm = pcm; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.Host == "oauth2.googleapis.com")
            {
                Refreshes++;
                if (ServiceAccountKey != null)
                {
                    var form = (await request.Content.ReadAsStringAsync()).Split('&').Select(part => part.Split('=', 2))
                        .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1].Replace('+', ' ')));
                    Check(form["grant_type"] == "urn:ietf:params:oauth:grant-type:jwt-bearer", "Wrong service account grant.");
                    var jwt = form["assertion"].Split('.');
                    byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
                    var claims = JObject.Parse(Encoding.UTF8.GetString(Decode(jwt[1])));
                    Check((string)claims["iss"] == "test@example.iam.gserviceaccount.com" &&
                        (string)claims["scope"] == "https://www.googleapis.com/auth/cloud-platform" &&
                        (string)claims["aud"] == "https://oauth2.googleapis.com/token" &&
                        (long)claims["exp"] - (long)claims["iat"] == 3600, "Wrong service account claims.");
                    Check(ServiceAccountKey.VerifyData(Encoding.UTF8.GetBytes(jwt[0] + "." + jwt[1]), Decode(jwt[2]),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "Invalid service account signature.");
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fake-token\",\"expires_in\":3600}") };
            }
            Requests++;
            var body = JObject.Parse(await request.Content.ReadAsStringAsync());
            Texts.Add((string)body["input"]["text"]);
            var legacy = body["voice"]["modelName"] == null;
            if (legacy)
                LegacyRequests++;
            if (legacy && ServiceAccountKey == null)
            {
                Check(request.Headers.GetValues("x-goog-api-key").Single() == "fake-key" && request.Headers.Authorization == null, "Legacy API key missing.");
            }
            else
            {
                Check(request.Headers.Authorization?.Parameter == "fake-token", "OAuth header missing.");
                Check(request.Headers.GetValues("x-goog-user-project").Single() == "fake-project", "Quota project missing.");
            }
            if (Requests == 2 && ServiceAccountKey == null) return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            return new HttpResponseMessage(Status) { Content = new StringContent(new JObject { ["audioContent"] = Convert.ToBase64String(Wav(pcm, legacy ? 48000 : 24000)) }.ToString()) };
        }
    }

    sealed class LiveDiagnosticHandler : DelegatingHandler
    {
        public LiveDiagnosticHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            // This opt-in probe sends only a fixed test phrase. Never print token responses.
            if (!response.IsSuccessStatusCode && request.RequestUri.Host == "texttospeech.googleapis.com")
            {
                var error = JObject.Parse(await response.Content.ReadAsStringAsync())["error"];
                Console.WriteLine($"Live TTS diagnostic: {error?["status"]}: {error?["message"]}");
            }
            return response;
        }
    }
}
