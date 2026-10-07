public class TTSConfigs : IConfig
{
    public string Type => "tts";
    public string GoogleApiKey { get; set; }
    public string OpenAiApiKey { get; set; }
    public string GoogleGeminiModel { get; set; } = "gemini-2.5-flash-tts";
    public string GoogleCredentialsPath { get; set; }
    public string GoogleProjectId { get; set; }
    public int MaxConcurrentRequests { get; set; } = 1;
}
