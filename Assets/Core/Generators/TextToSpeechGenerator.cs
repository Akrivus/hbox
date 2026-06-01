using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Audio;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public class TextToSpeechGenerator : MonoBehaviour, ISubGenerator
{
    private static string[] OpenAiVoices = new string[] { "alloy", "ash", "ballad", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer", "verse" };

    private static OpenAIClient api => _api ??= new OpenAIClient(new OpenAIAuthentication(TTS.OpenAiApiKey));
    private static OpenAIClient _api;

    public bool IsBlocking => false;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var tasks = new List<Task>();
        foreach (var node in chat.Nodes)
            tasks.Add(GenerateTextToSpeech(chat, node));
        await Task.WhenAll(tasks);
        return chat;
    }

    public async Task GenerateTextToSpeech(ChatNode node)
    {
        await GenerateTextToSpeech(null, node);
    }

    private async Task GenerateTextToSpeech(Chat chat, ChatNode node)
    {
        if (node.AudioData != null) return;
        if (OpenAiVoices.Contains(node.Actor.Voice))
            await GenerateWithOpenAI(chat, node);
        else
            await GenerateWithGoogle(chat, node);
    }

    private async Task GenerateWithGoogle(Chat chat, ChatNode node)
    {
        var attempts = 0;
        var success = node.AudioData != null;
        var stopwatch = Stopwatch.StartNew();
        string error = string.Empty;

        while (!success)
        {
            if (attempts > 30)
            {
                Debug.LogError("Failed to generate audio with Google TTS.");
                stopwatch.Stop();
                RecordTtsUsage(chat?.ManagerContext ?? ChatManagerContext.Current, chat?.FileName, "google-standard-tts", node.Say, node.Actor.Voice, attempts, false, stopwatch.ElapsedMilliseconds, error);
                return;
            }

            var response = await RequestFromGoogle(node.Say, node.Actor.Voice);
            success = response.IsSuccessStatusCode;
            error = success ? string.Empty : response.ReasonPhrase;

            if (success)
            {
                var text = await response.Content.ReadAsStringAsync();
                var output = JsonConvert.DeserializeObject<Output>(text);
                node.New = true;
                node.AudioData = output.AudioData;
            }

            success = success && node.AudioData != null;
            await Task.Delay(1000 * attempts++);
        }

        stopwatch.Stop();
        RecordTtsUsage(chat?.ManagerContext ?? ChatManagerContext.Current, chat?.FileName, "google-standard-tts", node.Say, node.Actor.Voice, attempts, true, stopwatch.ElapsedMilliseconds, string.Empty);
    }

    private async Task GenerateWithOpenAI(Chat chat, ChatNode node, int attempts = 0)
    {
        try
        {
            var clip = await GetClipFromOpenAI(node.Say, node.Actor.Voice, chat?.ManagerContext, chat?.FileName, attempts);
            if (clip == null)
                throw new Exception("No audio clip returned.");
            node.Frequency = clip.frequency;
            node.AudioClip = clip;

            node.New = true;
        }
        catch (Exception e)
        {
            if (attempts < 5)
                await GenerateWithOpenAI(chat, node, attempts + 1);
            else
                Debug.LogError(e.Message);
        }
    }

    private static async Task<HttpResponseMessage> RequestFromGoogle(string text, string voice)
    {
        var url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={TTS.GoogleApiKey}";
        var json = JsonConvert.SerializeObject(new Request(text, voice));

        var client = new HttpClient();
        return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    public static async Task<AudioClip> GetClipFromGoogle(string text, string voice)
    {
        if (string.IsNullOrEmpty(TTS.GoogleApiKey) || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(voice))
            return null;
        Debug.Log("Requesting Google TTS: " + text);
        var stopwatch = Stopwatch.StartNew();
        var response = await RequestFromGoogle(text, voice);
        if (!response.IsSuccessStatusCode)
        {
            stopwatch.Stop();
            RecordTtsUsage(ChatManagerContext.Current, null, "google-standard-tts", text, voice, 0, false, stopwatch.ElapsedMilliseconds, response.ReasonPhrase);
            return null;
        }
        var json = await response.Content.ReadAsStringAsync();
        var output = JsonConvert.DeserializeObject<Output>(json);
        stopwatch.Stop();
        RecordTtsUsage(ChatManagerContext.Current, null, "google-standard-tts", text, voice, 0, true, stopwatch.ElapsedMilliseconds, string.Empty);
        return output.AudioData.ToAudioClip();
    }

    public static async Task<AudioClip> GetClipFromOpenAI(string text, string voice)
    {
        return await GetClipFromOpenAI(text, voice, ChatManagerContext.Current, null, 0);
    }

    private static async Task<AudioClip> GetClipFromOpenAI(string text, string voice, ChatManagerContext context, string episodeSlug, int attempts)
    {
        if (string.IsNullOrEmpty(TTS.OpenAiApiKey) || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(voice))
            return null;
        Debug.Log("Requesting OpenAI TTS: " + text);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var clip = await api.AudioEndpoint.GetSpeechAsync(new SpeechRequest(text,
                voice: new OpenAI.Voice(voice),
                model: new OpenAI.Models.Model("gpt-4o-mini-tts"),
                responseFormat: SpeechResponseFormat.PCM));
            stopwatch.Stop();
            RecordTtsUsage(context, episodeSlug, "gpt-4o-mini-tts", text, voice, attempts, clip != null, stopwatch.ElapsedMilliseconds, clip == null ? "No audio clip returned." : string.Empty);
            return clip;
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            RecordTtsUsage(context, episodeSlug, "gpt-4o-mini-tts", text, voice, attempts, false, stopwatch.ElapsedMilliseconds, e.Message);
            throw;
        }
    }

    private static void RecordTtsUsage(ChatManagerContext context, string episodeSlug, string model, string text, string voice, int attempts, bool success, long durationMs, string error)
    {
        var chars = text?.Length ?? 0;
        LLM.RecordMeteredUsage(context, new LlmCallRecord
        {
            timestamp = DateTimeOffset.Now.ToString("O"),
            channelKey = context?.Key ?? string.Empty,
            context = context?.Name ?? string.Empty,
            episodeSlug = episodeSlug ?? string.Empty,
            templateName = $"TextToSpeechGenerator/{model}",
            profile = "TTS",
            model = model,
            callerType = nameof(TextToSpeechGenerator),
            callerMember = nameof(GenerateTextToSpeech),
            attempt = attempts,
            success = success,
            inputChars = chars,
            estimatedInputTokens = chars,
            billableUnitName = "chars",
            billableUnits = chars,
            usageType = "tts",
            durationMs = durationMs,
            error = error ?? string.Empty
        });
    }

    class Request
    {
        public TextInput input { get; set; }
        public AudioConfig audioConfig { get; set; }
        public Voice voice { get; set; }

        public Request(string text, string name)
        {
            audioConfig = new AudioConfig();
            input = new TextInput() { text = text.Scrub() };
            voice = new Voice() { name = name };
        }
    }

    class TextInput
    {
        public string text { get; set; }
    }

    class AudioConfig
    {
        public string audioEncoding { get; set; } = "LINEAR16";
        public float sampleRateHertz { get; set; } = 48000;
        public float volumeGainDb { get; set; } = 1;
        public float pitch { get; set; } = 1;
        public float speakingRate { get; set; } = 1.1f;
    }

    class Voice
    {
        public string name { get; set; } = "en-US-Standard-D";
        public string languageCode { get; set; } = "en-US";
    }

    class Output
    {
        [JsonProperty("audioContent")]
        public string AudioData { get; set; }
    }
}
