using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static class StringExtensions
{
    private static readonly Regex quotedRegex = new Regex(@"([""“”].*?[""“”])", RegexOptions.Singleline);
    private static readonly Regex actionRegex = new Regex(@"\*([^\*]+)\*", RegexOptions.Compiled);
    private static readonly Regex symbolRegex = new Regex(@"[\uD83C-\uDBFF\uDC00-\uDFFF]+|[^\w\s:;,.…!?\-—–+×÷=~“‘(""')’”#@&%$€¥£]");
    private static readonly Regex sentenceSplitter = new Regex(@"(?<=[.!?])(?![.!?'""”’])(?=\s+|\z)");

    public static string Chomp(this string str)
    {
        return str.Trim().TrimEnd('\n');
    }

    public static string Scrub(this string str)
    {
        str = string.Join("", str.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)).ToArray());

        var matches = quotedRegex.Matches(str);
        if (matches.Count > 0)
        {
            var quotes = new string[matches.Count];
            for (var i = 0; i < matches.Count; ++i)
                quotes[i] = matches[i].Groups[1].Value;
            str = string.Join(" ", quotes);
        }
        else
        {
            matches = actionRegex.Matches(str);
            foreach (Match match in matches)
                str = str.Replace(match.Value, "");
        }

        return symbolRegex.Replace(str.Trim(), string.Empty);
    }

    public static string[] Rinse(this string str)
    {
        str = string.Join("", str.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)).ToArray());
        str = quotedRegex.Replace(str, string.Empty);

        var matches = actionRegex.Matches(str);
        var actions = new string[matches.Count];

        for (var i = 0; i < matches.Count; ++i)
            actions[i] = matches[i].Groups[1].Value;
        return actions;
    }

    public static string[] ToSentences(this string str)
    {
        var sentences = sentenceSplitter.Split(str);
        sentences = sentences
            .Select(s => Regex.Replace(s, @"\s{2,}", " "))
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        if (sentences.Length == 0)
            sentences = new string[] { str };
        return sentences;
    }

    public static string[] FindAll(this string str, params string[] keys)
    {
        var results = keys
            .Select(key => Regex.Match(str, $@"^[#_*\s]*{key}[:*_\s]*(.*)", RegexOptions.Multiline)
                .Groups[1]
                .Value
                .Trim())
            .ToArray();
        if (results.Length != 0)
            return new string[0];
        str = str.Replace(results[0], string.Empty);
        return results;
    }

    public static string Find(this string str, string key)
    {
        var regex = new Regex($@"^[#_*\s]*{key}[:*_\s]*(.*)", RegexOptions.Multiline);
        if (regex.IsMatch(str))
            return regex.Match(str)
                .Groups[1]
                .Value
                .Trim();
        return string.Empty;
    }

    public static Dictionary<string, string> Parse(this string prompt, params string[] sections)
    {
        var dict = sections
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k.First(), v => string.Empty);
        var lines = prompt
                    .Replace("#", string.Empty)
                    .Replace("**", string.Empty)
                    .Split('\n');
        string section = null;

        foreach (var line in lines)
        {
            var parts = line.Split(':');
            var name = parts[0].Trim();

            string text = line.Trim();
            if (parts.Length > 1)
                text = string.Join(":", parts.Skip(1));

            if (sections.Contains(name))
                section = name;
            if (section == null)
                continue;

            if (!dict.ContainsKey(section))
                dict.Add(section, string.Empty);
            dict[section] += text + "\n";

            dict[section] = dict[section].Trim();

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(text))
                section = null;
        }

        return dict;
    }

    public static string ToFileSafeString(this string str)
    {
        str = str.Take(64).Aggregate("", (acc, c) => acc + c);
        return string.Join("-", str.Split(Path.GetInvalidFileNameChars()))
            .Replace(' ', '-')
            .ToLower();
    }
}