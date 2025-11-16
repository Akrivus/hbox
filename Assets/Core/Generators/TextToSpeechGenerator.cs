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

public class TextToSpeechGenerator : MonoBehaviour, ISubGenerator
{
    private static string[] OpenAiVoices = new string[] { "alloy", "ash", "ballad", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer", "verse" };

    private static OpenAIClient api => _api ??= new OpenAIClient(new OpenAIAuthentication(TTS.OpenAiApiKey));
    private static OpenAIClient _api;

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var tasks = new List<Task>();
        foreach (var node in chat.Nodes)
            tasks.Add(GenerateTextToSpeech(node));
        await Task.WhenAll(tasks);
        return chat;
    }

    public async Task GenerateTextToSpeech(ChatNode node)
    {
        if (node.AudioData != null) return;
        if (OpenAiVoices.Contains(node.Actor.Voice))
            await GenerateWithOpenAI(node);
        else
            await GenerateWithGoogle(node);
    }

    private async Task GenerateWithGoogle(ChatNode node)
    {
        var attempts = 0;
        var success = node.AudioData != null;

        while (!success)
        {
            if (attempts > 30)
            {
                Debug.LogError("Failed to generate audio with Google TTS.");
                return;
            }

            var response = await RequestFromGoogle(node.Say, node.Actor.Voice);
            success = response.IsSuccessStatusCode;

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
    }

    private async Task GenerateWithOpenAI(ChatNode node, int attempts = 0)
    {
        try
        {
            var clip = await GetClipFromOpenAI(node.Say, node.Actor.Voice);
            node.Frequency = clip.frequency;
            node.AudioClip = clip;

            node.New = true;
        }
        catch (Exception)
        {
            if (attempts < 5)
                await GenerateWithOpenAI(node, attempts + 1);
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
        var response = await RequestFromGoogle(text, voice);
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync();
        var output = JsonConvert.DeserializeObject<Output>(json);
        return output.AudioData.ToAudioClip();
    }

    public static async Task<AudioClip> GetClipFromOpenAI(string text, string voice)
    {
        if (string.IsNullOrEmpty(TTS.GoogleApiKey) || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(voice))
            return null;
        Debug.Log("Requesting OpenAI TTS: " + text);
        return await api.AudioEndpoint.GetSpeechAsync(new SpeechRequest(text,
            voice: new OpenAI.Voice(voice),
            model: new OpenAI.Models.Model("gpt-4o-mini-tts"),
            responseFormat: SpeechResponseFormat.PCM));
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