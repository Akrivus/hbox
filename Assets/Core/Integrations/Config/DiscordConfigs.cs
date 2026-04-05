using System.Collections.Generic;

public class DiscordConfigs : IConfig
{
    public string Type => "discord";
    public Dictionary<string, string> WebhookURLs { get; set; }
    public string AvatarURL { get; set; }
    public bool EnableBot { get; set; }
    public string BotToken { get; set; }
    public string GatewayURL { get; set; } = "wss://gateway.discord.gg/?v=10&encoding=json";
    public string ApplicationId { get; set; }
    public string[] SlashCommandGuildIds { get; set; }
    public bool EnableIdeaCommand { get; set; } = true;
    public int DefaultDailyIdeaLimit { get; set; } = 3;
    public int BoosterDailyIdeaLimit { get; set; } = 10;
    public string[] BoosterRoleIds { get; set; }
}
