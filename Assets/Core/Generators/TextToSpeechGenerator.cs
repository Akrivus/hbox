using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Audio;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public class TextToSpeechGenerator : MonoBehaviour, ISubGenerator
{
    private const int OpenAiPcmSampleRate = 24000;

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

    // Explicit short auditions share production routing and metering, without automatic retries.
    public static Task GenerateAudition(ChatNode node, TTSConfigs settings) => GenerateAudition(node, settings, null);

    // Injectable transport for deterministic checks; the default uses the shared production client.
    internal static async Task GenerateAudition(ChatNode node, TTSConfigs settings, GoogleTtsClient googleClient)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (node?.Actor == null || string.IsNullOrWhiteSpace(node.Say) ||
            System.Text.Encoding.UTF8.GetByteCount(node.Say.Scrub()) > 500)
            throw new ArgumentException("Select an actor and a phrase of at most 500 UTF-8 bytes.");
        if (GoogleTtsClient.IsOpenAiVoice(node.Actor.Voice))
        {
            if (string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
                throw new InvalidOperationException("OpenAI credentials are missing.");
            var client = new OpenAIClient(new OpenAIAuthentication(settings.OpenAiApiKey));
            var audio = await GetAudioDataFromOpenAI(node.Say, node.Actor.Voice, null, null, 0, client, settings.OpenAiApiKey);
            if (audio == null) throw new InvalidOperationException("No audio returned. Check OpenAI credentials.");
            node.AudioData = audio.AudioData;
            node.Frequency = audio.Frequency;
        }
        else
            await GenerateWithGoogle(null, node, GoogleTtsClient.HasGeminiPrefix(node.Actor.Voice), 0, settings, true, googleClient);
    }

    private async Task GenerateTextToSpeech(Chat chat, ChatNode node)
    {
        if (node == null || node.HasPlayableAudio)
            return;

        var existingTask = node.AudioGenerationTask;
        if (existingTask != null)
        {
            await existingTask;
            return;
        }

        var task = GenerateTextToSpeechCore(chat, node);
        node.SetAudioGenerationTask(task);
        try
        {
            await task;
        }
        finally
        {
            node.ClearAudioGenerationTask(task);
        }
    }

    private async Task GenerateTextToSpeechCore(Chat chat, ChatNode node)
    {

        if (GoogleTtsClient.IsOpenAiVoice(node.Actor.Voice))
            await GenerateWithOpenAI(chat, node);
        else
            await GenerateWithGoogle(chat, node, GoogleTtsClient.HasGeminiPrefix(node.Actor.Voice));
    }

    private static readonly GoogleTtsClient google = new GoogleTtsClient(new HttpClient());

    private static async Task GenerateWithGoogle(Chat chat, ChatNode node, bool gemini, int maxRetries = 4, TTSConfigs settings = null, bool audition = false, GoogleTtsClient googleClient = null)
    {
        settings = settings ?? TTS.Settings;
        var voice = gemini ? GoogleTtsClient.NormalizeGeminiVoice(node.Actor.Voice) : node.Actor.Voice;
        var model = gemini
            ? (string.IsNullOrWhiteSpace(node.Actor.VoiceModel) ? settings.GoogleGeminiModel : node.Actor.VoiceModel)
            : "google-standard-tts";
        var prompt = string.Join("\n", new[] { node.Actor.VoiceInstructions, node.VoiceInstructions }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var watch = Stopwatch.StartNew();
        try
        {
            var audio = await GenerateGoogleAudio(node.Say, voice, model, prompt, node.Actor.VoiceLanguage, gemini, maxRetries, settings, googleClient);
            node.AudioData = audio.AudioData;
            node.Frequency = audio.Frequency;
            node.New = true;
            RecordTtsUsage(audition ? null : chat?.ManagerContext ?? ChatManagerContext.Current, chat?.FileName, model,
                node.Say, voice, 1, true, watch.ElapsedMilliseconds, string.Empty, prompt, audio);
        }
        catch (Exception e)
        {
            RecordTtsUsage(audition ? null : chat?.ManagerContext ?? ChatManagerContext.Current, chat?.FileName, model,
                node.Say, voice, 1, false, watch.ElapsedMilliseconds, e.Message, prompt);
            Debug.LogError(e.Message);
            throw;
        }
    }

    private static async Task<GeneratedAudioData> GenerateGoogleAudio(string text, string voice, string model, string prompt, string language, bool gemini, int maxRetries = 4, TTSConfigs settings = null, GoogleTtsClient googleClient = null)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(voice))
            throw new ArgumentException("Google TTS requires text and voice.");
        await TTS.WaitForRequestSlot();
        try
        {
            var bytes = await (googleClient ?? google).Synthesize(text.Scrub(), voice, model, prompt, language, settings ?? TTS.Settings, gemini, maxRetries);
            return new GeneratedAudioData(Convert.ToBase64String(bytes), gemini ? 24000 : 48000);
        }
        finally { TTS.ReleaseRequestSlot(); }
    }
    private async Task GenerateWithOpenAI(Chat chat, ChatNode node, int attempts = 0)
    {
        try
        {
            var audio = await GetAudioDataFromOpenAI(node.Say, node.Actor.Voice, chat?.ManagerContext, chat?.FileName, attempts);
            if (audio == null || string.IsNullOrEmpty(audio.AudioData))
                throw new Exception("No audio data returned.");
            node.Frequency = audio.Frequency;
            node.AudioData = audio.AudioData;

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

    public static async Task<AudioClip> GetClipFromGoogle(string text, string voice)
    {
        var audio = await GetAudioDataFromGoogle(text, voice);
        return audio?.AudioData.ToAudioClip(audio.Frequency);
    }

    public static async Task<GeneratedAudioData> GetAudioDataFromGoogle(string text, string voice)
    {
        return await GetGoogleAudioData(text, voice, false, null);
    }

    public static async Task<AudioClip> GetClipFromGemini(string text, string voice, string instructions = null)
    {
        var audio = await GetAudioDataFromGemini(text, voice, instructions);
        return audio?.AudioData.ToAudioClip(audio.Frequency);
    }

    public static Task<GeneratedAudioData> GetAudioDataFromGemini(string text, string voice, string instructions = null)
    {
        return GetGoogleAudioData(text, GoogleTtsClient.NormalizeGeminiVoice(voice), true, instructions);
    }

    private static async Task<GeneratedAudioData> GetGoogleAudioData(string text, string voice, bool gemini, string instructions)
    {
        var model = gemini ? TTS.Settings.GoogleGeminiModel : "google-standard-tts";
        var watch = Stopwatch.StartNew();
        try
        {
            var audio = await GenerateGoogleAudio(text, voice, model, instructions, "en-US", gemini);
            RecordTtsUsage(ChatManagerContext.Current, null, model, text, voice, 1, true, watch.ElapsedMilliseconds, string.Empty, instructions, audio);
            return audio;
        }
        catch (Exception e)
        {
            RecordTtsUsage(ChatManagerContext.Current, null, model, text, voice, 1, false, watch.ElapsedMilliseconds, e.Message);
            throw;
        }
    }
    public static async Task<AudioClip> GetClipFromOpenAI(string text, string voice)
    {
        var audio = await GetAudioDataFromOpenAI(text, voice, ChatManagerContext.Current, null, 0);
        return audio?.AudioData.ToAudioClip(audio.Frequency);
    }

    private static async Task<GeneratedAudioData> GetAudioDataFromOpenAI(string text, string voice, ChatManagerContext context, string episodeSlug, int attempts, OpenAIClient clientOverride = null, string apiKeyOverride = null)
    {
        if (string.IsNullOrEmpty(apiKeyOverride ?? TTS.OpenAiApiKey) || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(voice))
            return null;
        Debug.Log("Requesting OpenAI TTS: " + text);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            SpeechClip speechClip;
            await TTS.WaitForRequestSlot();
            try
            {
                speechClip = await (clientOverride ?? api).AudioEndpoint.GetSpeechAsync(new SpeechRequest(text,
                    voice: new OpenAI.Voice(voice),
                    model: new OpenAI.Models.Model("gpt-4o-mini-tts"),
                    responseFormat: SpeechResponseFormat.PCM));
            }
            finally
            {
                TTS.ReleaseRequestSlot();
            }
            stopwatch.Stop();
            var audioData = speechClip?.AudioData.ToArray();
            var hasAudio = audioData != null && audioData.Length > 0;
            speechClip?.Dispose();
            RecordTtsUsage(context, episodeSlug, "gpt-4o-mini-tts", text, voice, attempts, hasAudio, stopwatch.ElapsedMilliseconds, hasAudio ? string.Empty : "No audio data returned.");
            return hasAudio ? new GeneratedAudioData(Convert.ToBase64String(audioData), OpenAiPcmSampleRate) : null;
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            RecordTtsUsage(context, episodeSlug, "gpt-4o-mini-tts", text, voice, attempts, false, stopwatch.ElapsedMilliseconds, e.Message);
            throw;
        }
    }

    private static void RecordTtsUsage(ChatManagerContext context, string episodeSlug, string model, string text, string voice, int attempts, bool success, long durationMs, string error, string prompt = null, GeneratedAudioData audio = null)
    {
        var chars = text?.Length ?? 0;
        var gemini = model != null && model.StartsWith("gemini-", StringComparison.Ordinal);
        var chunkCount = GoogleTtsClient.SplitText(text ?? string.Empty).Count();
        var inputTokens = gemini ? (int)Math.Ceiling((chars + (prompt?.Length ?? 0) * chunkCount) / 4d) : chars;
        var audioTokens = gemini && audio != null
            ? (int)Math.Ceiling(Convert.FromBase64String(audio.AudioData).Length / (2d * audio.Frequency) * 25d) : 0;
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
            estimatedInputTokens = inputTokens,
            estimatedOutputTokens = audioTokens,
            promptTokens = gemini ? inputTokens : 0,
            completionTokens = audioTokens,
            completionAudioTokens = audioTokens,
            totalTokens = gemini ? inputTokens + audioTokens : 0,
            billableUnitName = gemini ? "estimated_tokens" : "chars",
            billableUnits = gemini ? 0 : chars,
            usageType = "tts",
            durationMs = durationMs,
            error = error ?? string.Empty
        });
    }

    public sealed class GeneratedAudioData
    {
        public string AudioData { get; }
        public int Frequency { get; }

        public GeneratedAudioData(string audioData, int frequency)
        {
            AudioData = audioData;
            Frequency = frequency;
        }
    }
}
