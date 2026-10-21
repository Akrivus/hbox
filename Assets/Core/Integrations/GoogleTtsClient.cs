using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Request LINEAR16 for Cloud TTS and strip WAV headers before Unity playback.
public sealed class GoogleTtsClient
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim tokenGate = new SemaphoreSlim(1, 1);
    private string token;
    private string tokenPath;
    private string quotaProjectId;
    private DateTimeOffset tokenExpires;

    public GoogleTtsClient(HttpClient client) { this.client = client; }

    public static bool HasGeminiPrefix(string voice) => voice != null && voice.StartsWith("gemini:", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeGeminiVoice(string voice) => HasGeminiPrefix(voice) ? voice.Substring(7) : voice;

    public static bool IsOpenAiVoice(string voice)
    {
        switch (voice)
        {
            case "alloy": case "ash": case "ballad": case "coral": case "echo":
            case "fable": case "onyx": case "nova": case "sage": case "shimmer": case "verse":
                return true;
            default: return false;
        }
    }

    public static bool IsGeminiVoice(string voice) => !string.IsNullOrWhiteSpace(voice) && !voice.Contains("-");

    public static JObject CreateRequest(string text, string voice, string model, string prompt, string language, bool gemini)
    {
        if (string.IsNullOrWhiteSpace(voice)) throw new ArgumentException("Google TTS voice is missing.");

        if (gemini != IsGeminiVoice(voice))
            throw new ArgumentException("Choose a Gemini voice such as Kore for GoogleGemini, or a Cloud voice such as en-US-Standard-D for Google.");
        var input = new JObject { ["text"] = text };
        if (gemini && !string.IsNullOrWhiteSpace(prompt))
        {
            if (Encoding.UTF8.GetByteCount(prompt) > 4000)
                throw new ArgumentException("Gemini TTS direction exceeds 4,000 UTF-8 bytes.");
            input["prompt"] = prompt;
        }
        var selection = new JObject { ["name"] = voice, ["languageCode"] = string.IsNullOrWhiteSpace(language) ? "en-US" : language };
        if (gemini) selection["modelName"] = model;
        var audio = new JObject { ["audioEncoding"] = "LINEAR16", ["sampleRateHertz"] = gemini ? 24000 : 48000 };
        if (!gemini)
        {
            audio["volumeGainDb"] = 1;
            audio["pitch"] = 1;
            audio["speakingRate"] = 1.1;
        }
        return new JObject { ["input"] = input, ["voice"] = selection, ["audioConfig"] = audio };
    }

    public static IEnumerable<string> SplitText(string text, int maxBytes = 4000)
    {
        if (maxBytes < 4) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        while (!string.IsNullOrEmpty(text))
        {
            var end = 0;
            var bytes = 0;
            var boundary = 0;
            while (end < text.Length)
            {
                var width = char.IsHighSurrogate(text[end]) && end + 1 < text.Length && char.IsLowSurrogate(text[end + 1]) ? 2 : 1;
                var next = Encoding.UTF8.GetByteCount(text.Substring(end, width));
                if (bytes + next > maxBytes) break;
                bytes += next;
                end += width;
                if (char.IsWhiteSpace(text[end - 1])) boundary = end;
            }
            if (end < text.Length && boundary > end / 2) end = boundary;
            yield return text.Substring(0, end);
            text = text.Substring(end);
        }
    }

    public async Task<byte[]> Synthesize(string text, string voice, string model, string prompt, string language, TTSConfigs settings, bool gemini, int maxRetries = 4)
    {

        using (var output = new MemoryStream())
        {
            foreach (var chunk in SplitText(text))
            {
                var body = CreateRequest(chunk, voice, model, prompt, language, gemini);
                // Retry only transient failures; never retry successful chunks of a long utterance.
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Post, "https://texttospeech.googleapis.com/v1/text:synthesize"))
                        {
                            if (gemini || string.IsNullOrWhiteSpace(settings.GoogleApiKey))
                            {
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetToken(settings));
                                var project = string.IsNullOrWhiteSpace(settings.GoogleProjectId) ? quotaProjectId : settings.GoogleProjectId;
                                if (!string.IsNullOrWhiteSpace(project))
                                    request.Headers.Add("x-goog-user-project", project);
                            }
                            else
                            {
                                if (string.IsNullOrWhiteSpace(settings.GoogleApiKey)) throw new InvalidOperationException("Google TTS API key is missing.");
                                request.Headers.Add("x-goog-api-key", settings.GoogleApiKey);
                            }
                            request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                            using (var response = await client.SendAsync(request))
                            {
                                if (!response.IsSuccessStatusCode)
                                {
                                    if (attempt < maxRetries && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
                                    {
                                        await Task.Delay(1000 * (1 << attempt));
                                        continue;
                                    }
                                    // Do not log the response body: it can contain text or credential details.
                                    throw new InvalidOperationException($"Google TTS failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Check credentials, project permissions, model and voice.");
                                }
                                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                                var encoded = (string)json["audioContent"];
                                if (string.IsNullOrEmpty(encoded)) throw new InvalidOperationException("Google TTS returned no audio.");
                                var pcm = DecodeAudio(Convert.FromBase64String(encoded), gemini ? 24000 : 48000);
                                output.Write(pcm, 0, pcm.Length);
                                break;
                            }
                        }
                    }
                    catch (HttpRequestException) when (attempt < maxRetries) { await Task.Delay(1000 * (1 << attempt)); }
                    catch (TaskCanceledException) when (attempt < maxRetries) { await Task.Delay(1000 * (1 << attempt)); }
                }
            }
            return output.ToArray();
        }
    }

    public static byte[] DecodeAudio(byte[] bytes, int expectedRate)
    {
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE")
        {
            var validFormat = false;
            byte[] pcm = null;
            for (var offset = 12; offset + 8 <= bytes.Length;)
            {
                var size = BitConverter.ToUInt32(bytes, offset + 4);
                if (size > int.MaxValue || (long)offset + 8 + size > bytes.Length) throw new InvalidOperationException("Truncated Google TTS WAV.");
                var id = Encoding.ASCII.GetString(bytes, offset, 4);
                if (id == "fmt " && size >= 16)
                    validFormat = BitConverter.ToUInt16(bytes, offset + 8) == 1 && BitConverter.ToUInt16(bytes, offset + 10) == 1 &&
                        BitConverter.ToInt32(bytes, offset + 12) == expectedRate && BitConverter.ToUInt16(bytes, offset + 22) == 16;
                if (id == "data") pcm = bytes.Skip(offset + 8).Take((int)size).ToArray();
                offset += 8 + (int)size + (int)(size % 2);
            }
            if (!validFormat || pcm == null) throw new InvalidOperationException("Google TTS WAV must be mono PCM16 at the requested sample rate.");
            bytes = pcm;
        }
        if (bytes.Length == 0 || bytes.Length % 2 != 0) throw new InvalidOperationException("Google TTS returned invalid PCM16 audio.");
        return bytes;
    }

    private async Task<string> GetToken(TTSConfigs settings)
    {
        var path = ResolveCredentialsPath(settings);
        await tokenGate.WaitAsync();
        try
        {
            if (tokenPath == path && !string.IsNullOrEmpty(token) && DateTimeOffset.UtcNow < tokenExpires) return token;
            if (!File.Exists(path)) throw new InvalidOperationException("Gemini TTS requires Google OAuth credentials. Run gcloud auth application-default login or configure GoogleCredentialsPath.");
            var credentials = JObject.Parse(File.ReadAllText(path));
            quotaProjectId = (string)credentials["quota_project_id"] ?? (string)credentials["project_id"];
            Dictionary<string, string> form;
            switch ((string)credentials["type"])
            {
                case "authorized_user":
                    form = new Dictionary<string, string>
                    {
                        { "grant_type", "refresh_token" }, { "client_id", (string)credentials["client_id"] },
                        { "client_secret", (string)credentials["client_secret"] }, { "refresh_token", (string)credentials["refresh_token"] }
                    };
                    break;
                case "service_account":
                    form = new Dictionary<string, string>
                    {
                        { "grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer" },
                        { "assertion", CreateServiceAccountAssertion(credentials) }
                    };
                    break;
                default:
                    throw new InvalidOperationException("Google TTS requires authorized_user or service_account credentials.");
            }
            using (var response = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form)))
            {
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Google OAuth token exchange failed: HTTP {(int)response.StatusCode}. Check the configured credentials and whether the account/key is enabled.");
                var result = JObject.Parse(await response.Content.ReadAsStringAsync());
                token = (string)result["access_token"];
                if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Google OAuth returned no access token.");
                tokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, ((int?)result["expires_in"] ?? 3600) - 60));
                tokenPath = path;
                return token;
            }
        }
        finally { tokenGate.Release(); }
    }

    public static string ResolveCredentialsPath(TTSConfigs settings)
    {
        var path = settings.GoogleCredentialsPath;
        if (string.IsNullOrWhiteSpace(path)) path = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        if (string.IsNullOrWhiteSpace(path) && File.Exists("google-credentials.json")) path = "google-credentials.json";
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gcloud", "application_default_credentials.json");
        return Path.GetFullPath(path);
    }

    private static string CreateServiceAccountAssertion(JObject credentials)
    {
        var email = (string)credentials["client_email"];
        var pem = (string)credentials["private_key"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pem))
            throw new InvalidOperationException("Google service-account credentials are missing client_email or private_key.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new JObject { ["alg"] = "RS256", ["typ"] = "JWT" };
        if (!string.IsNullOrWhiteSpace((string)credentials["private_key_id"])) header["kid"] = credentials["private_key_id"];
        var claims = new JObject
        {
            ["iss"] = email, ["scope"] = "https://www.googleapis.com/auth/cloud-platform",
            ["aud"] = "https://oauth2.googleapis.com/token", ["iat"] = now, ["exp"] = now + 3600
        };
        var unsigned = Base64Url(Encoding.UTF8.GetBytes(header.ToString(Formatting.None))) + "." +
            Base64Url(Encoding.UTF8.GetBytes(claims.ToString(Formatting.None)));
        using (var rsa = RSA.Create())
        {
            var key = Convert.FromBase64String(pem.Replace("-----BEGIN PRIVATE KEY-----", "").Replace("-----END PRIVATE KEY-----", ""));
            try
            {
                // Unity Mono exposes ImportPkcs8PrivateKey but throws PlatformNotSupportedException.
                // Import the standard RSA parameters instead; never send private keys to a helper process.
                var parameters = ReadPkcs8RsaParameters(key);
                try { rsa.ImportParameters(parameters); }
                finally
                {
                    foreach (var part in new[] { parameters.D, parameters.P, parameters.Q, parameters.DP, parameters.DQ, parameters.InverseQ })
                        if (part != null) Array.Clear(part, 0, part.Length);
                }
                return unsigned + "." + Base64Url(rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
            finally { Array.Clear(key, 0, key.Length); }
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Restricted DER reader for unencrypted PKCS#8 rsaEncryption keys from Google service accounts.
    internal static RSAParameters ReadPkcs8RsaParameters(byte[] key)
    {
        var outer = new KeyDerReader(key);
        var pkcs8 = outer.Read(0x30);
        outer.RequireEnd();
        pkcs8.RequireZeroVersion();
        var algorithm = pkcs8.Read(0x30);
        var oid = algorithm.Read(0x06).Bytes();
        if (!oid.SequenceEqual(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 }))
            throw new CryptographicException("Google service-account private key must be RSA.");
        if (!algorithm.End) algorithm.Read(0x05).RequireEnd();
        algorithm.RequireEnd();
        var privateKey = pkcs8.Read(0x04);
        pkcs8.RequireEnd();
        var rsa = privateKey.Read(0x30);
        privateKey.RequireEnd();
        rsa.RequireZeroVersion();
        var modulus = rsa.Integer();
        var exponent = rsa.Integer();
        var result = new RSAParameters
        {
            Modulus = modulus, Exponent = exponent, D = rsa.Integer(modulus.Length),
            P = rsa.Integer((modulus.Length + 1) / 2), Q = rsa.Integer((modulus.Length + 1) / 2),
            DP = rsa.Integer((modulus.Length + 1) / 2), DQ = rsa.Integer((modulus.Length + 1) / 2),
            InverseQ = rsa.Integer((modulus.Length + 1) / 2)
        };
        rsa.RequireEnd();
        return result;
    }

    private sealed class KeyDerReader
    {
        private readonly byte[] data;
        private int position;
        private readonly int end;
        public bool End => position == end;
        public KeyDerReader(byte[] data) : this(data, 0, data.Length) { }
        private KeyDerReader(byte[] data, int start, int end) { this.data = data; position = start; this.end = end; }
        private byte Next()
        {
            if (position >= end) throw new CryptographicException("Truncated service-account private key.");
            return data[position++];
        }
        public KeyDerReader Read(byte tag)
        {
            if (Next() != tag) throw new CryptographicException("Invalid service-account private key format.");
            var first = Next();
            var length = (int)first;
            if ((first & 0x80) != 0)
            {
                var count = first & 0x7f;
                if (count == 0 || count > 4) throw new CryptographicException("Invalid private key length.");
                length = 0;
                for (var i = 0; i < count; i++)
                {
                    if (length > int.MaxValue >> 8) throw new CryptographicException("Invalid private key length.");
                    length = (length << 8) | Next();
                }
            }
            if (length < 0 || length > end - position) throw new CryptographicException("Truncated service-account private key.");
            var value = new KeyDerReader(data, position, position + length);
            position += length;
            return value;
        }
        public byte[] Bytes()
        {
            var bytes = new byte[end - position];
            Buffer.BlockCopy(data, position, bytes, 0, bytes.Length);
            position = end;
            return bytes;
        }
        public byte[] Integer(int width = 0)
        {
            var value = Read(0x02);
            if (value.End || (value.data[value.position] & 0x80) != 0) throw new CryptographicException("Invalid RSA integer.");
            while (value.end - value.position > 1 && value.data[value.position] == 0) value.position++;
            var length = value.end - value.position;
            if (width == 0) width = length;
            if (length > width) throw new CryptographicException("Invalid RSA parameter width.");
            var bytes = new byte[width];
            Buffer.BlockCopy(data, value.position, bytes, width - length, length);
            return bytes;
        }
        public void RequireZeroVersion()
        {
            var version = Integer();
            if (version.Length != 1 || version[0] != 0) throw new CryptographicException("Unsupported private key version.");
        }
        public void RequireEnd()
        {
            if (!End) throw new CryptographicException("Unexpected data in service-account private key.");
        }
    }
}
