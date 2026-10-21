using System.Text.RegularExpressions;

public static class WebSourcePrompts
{
    public static string Format(string template, params string[] values) => Regex.Replace(template, @"\{(\d+)\}", match =>
        int.TryParse(match.Groups[1].Value, out var index) && index < values.Length ? values[index] ?? string.Empty : match.Value);
}
