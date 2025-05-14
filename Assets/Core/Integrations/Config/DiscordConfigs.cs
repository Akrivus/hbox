using System;
using System.Collections.Generic;

public class DiscordConfigs : IConfig
{
    public string Type => "discord";
    public Dictionary<string, string> WebhookURLs { get; set; }
    public string AvatarURL { get; set; }
}