// Compile-only collaborators; provider requests use the real Google adapter with fake HTTP.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
public interface ISubGenerator { }
public class PromptResolver { }
public class Chat { public List<ChatNode> Nodes; public ChatManagerContext ManagerContext; public string FileName; }
public class ChatNode
{
    public Actor Actor;
    public string Say, VoiceInstructions, AudioData;
    public bool New;
    public int Frequency;
    public bool HasPlayableAudio => !string.IsNullOrEmpty(AudioData);
    public Task AudioGenerationTask;
    public void SetAudioGenerationTask(Task task) { AudioGenerationTask = task; }
    public void ClearAudioGenerationTask(Task task) { AudioGenerationTask = null; }
}
public static class TTS
{
    // Deliberately different global config: explicit audition settings must win.
    public static TTSConfigs Settings = new TTSConfigs { GoogleGeminiModel = "wrong-global-model" };
    public static string OpenAiApiKey;
    public static Task WaitForRequestSlot() => Task.CompletedTask;
    public static void ReleaseRequestSlot() { }
}
public static class AudioExtensions
{
    public static string Scrub(this string text) => text;
    public static AudioClip ToAudioClip(this string data, int frequency) => new AudioClip();
}
public class LlmCallRecord
{
    public string timestamp, channelKey, context, episodeSlug, templateName, profile, model, callerType, callerMember,
        billableUnitName, usageType, error;
    public bool success;
    public int attempt, inputChars, estimatedInputTokens, estimatedOutputTokens, promptTokens, completionTokens,
        completionAudioTokens, totalTokens, billableUnits;
    public long durationMs;
}
public static class LLM { public static void RecordMeteredUsage(ChatManagerContext context, LlmCallRecord record) { } }
namespace OpenAI
{
    public class OpenAIAuthentication { public OpenAIAuthentication(string key) { } }
    public class Voice { public Voice(string voice) { } }
    public class OpenAIClient
    {
        public OpenAIClient(OpenAIAuthentication auth) { }
        public Audio.Endpoint AudioEndpoint => new Audio.Endpoint();
    }
}
namespace OpenAI.Models { public class Model { public Model(string name) { } } }
namespace OpenAI.Audio
{
    public enum SpeechResponseFormat { PCM }
    public class SpeechRequest
    { public SpeechRequest(string text, OpenAI.Voice voice, OpenAI.Models.Model model, SpeechResponseFormat responseFormat) { } }
    public class SpeechClip : IDisposable { public List<byte> AudioData; public void Dispose() { } }
    public class Endpoint
    {
        public Task<SpeechClip> GetSpeechAsync(SpeechRequest request) => throw new Exception("No live OpenAI calls allowed in deterministic checks");
    }
}
