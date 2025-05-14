using System;

public class TTSConfigs : IConfig
{
    public string Type => "tts";
    public string GoogleApiKey { get; set; }
    public string OpenAiApiKey { get; set; }
}