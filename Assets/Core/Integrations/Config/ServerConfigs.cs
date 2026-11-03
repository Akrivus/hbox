using System;
using System.Text.RegularExpressions;

// Machine-wide settings: read from hbox.json once, independently of channel configs.
public class ServerConfigs : IConfig
{
    public string Type => "server";
    public int Port { get; set; } = 6789;
    public bool EnableMdns { get; set; } = true;
    public string HostName { get; set; } = "hbox";

    public void Validate()
    {
        if (Port < 1 || Port > 65535)
            throw new ArgumentException("Server Port must be between 1 and 65535.");
        if (EnableMdns && (HostName == null || !Regex.IsMatch(HostName,
                @"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\z")))
            throw new ArgumentException("Server HostName must be a DNS label of 1-63 letters, digits or hyphens, without .local or leading/trailing hyphens.");
    }

    public string LocalUrl => Port == 80 ? "http://localhost" : $"http://localhost:{Port}";
    public string MdnsUrl => Port == 80 ? $"http://{HostName}.local" : $"http://{HostName}.local:{Port}";
}
