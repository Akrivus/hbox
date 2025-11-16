using System.Collections.Generic;

public class ServerConfigs : IConfig
{
    public string Type => "premier";
    public List<string> Prompts { get; set; }
}