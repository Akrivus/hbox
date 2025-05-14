using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class RemoteControlConfigs : IConfig
{
    public string Type => "remote_control";
    public string PageDownPath { get; set; }
    public string PageUpPath { get; set; }
}