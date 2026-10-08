using System;

public static class SoccerModeMetadata
{
    public static bool IsMatchRequest(string topic, string idea, string source)
    {
        if (string.Equals(source, "soccer-postgame", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Read(idea, "SoccerPhase"), "Postgame", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(Read(topic, "Mode"), "Soccer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Read(idea, "Mode"), "Soccer", StringComparison.OrdinalIgnoreCase);
    }
    public static string Read(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key)) return string.Empty;
        foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim().TrimStart('#', '_', '*', '`').TrimStart();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = line.Substring(key.Length).TrimStart().TrimStart('*', '_', '`').TrimStart();
            if (value.Length == 0 || value[0] != ':') continue;
            return value.Substring(1).Trim(' ', '\t', '*', '_', '`');
        }
        return string.Empty;
    }
}
