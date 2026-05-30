using System.Collections.Generic;

public class OpenAIConfigs : IConfig
{
    public string Type => "openai";
    public bool UseEmbeddings { get; set; } = false;
    public string ApiUri { get; set; }
    public string ApiKey { get; set; }
    public string SlowModel { get; set; }
    public string FastModel { get; set; }
    public Dictionary<string, string> ModelProfiles { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, LlmModelPrice> ModelPrices { get; set; } = new Dictionary<string, LlmModelPrice>();
    public bool PersistUsage { get; set; } = true;
    public string UsageLogPath { get; set; } = "Logs/llm-usage";
    public LlmBudgetPolicy Budgets { get; set; } = new LlmBudgetPolicy();
}

public class LlmModelPrice
{
    public double InputPerMillion { get; set; }
    public double CachedInputPerMillion { get; set; }
    public double OutputPerMillion { get; set; }
}

public class LlmBudgetPolicy
{
    public double DailyUsd { get; set; }
    public double PerEpisodeUsd { get; set; }
    public double WarnAtPercent { get; set; } = 80;
    public bool EnableUiWarnings { get; set; } = true;
    public bool EnableDiscordWarnings { get; set; } = false;
    public string DiscordChannel { get; set; }
}
