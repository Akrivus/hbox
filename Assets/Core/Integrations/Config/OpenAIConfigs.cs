using System;

public class OpenAIConfigs : IConfig
{
    public string Type => "openai";
    public string ApiUri { get; set; }
    public string ApiKey { get; set; }
    public string SlowModel { get; set; }
    public string FastModel { get; set; }
}