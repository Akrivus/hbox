using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ServerConfigs : IConfig
{
    public string Type => "premier";
    public List<string> Prompts { get; set; }
}