using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Shared source/Idea/replay asset. Embedded bytes keep replays independent of CDN URLs.
[Serializable]
public sealed class ImageAttachment
{
    public string Id { get; set; }
    public string Url { get; set; }
    public string SourceUrl { get; set; }
    public string SourceLabel { get; set; }
    public string Caption { get; set; }
    public string MimeType { get; set; }
    public string DataBase64 { get; set; }
    public string Sha256 { get; set; }
    public string RetrievedAtUtc { get; set; }
    public string Analysis { get; set; }
    public string DownloadError { get; set; }
    public string AnalysisError { get; set; }
    public bool UseForGeneration { get; set; } = true;
    public bool PresentationEligible { get; set; }
    public bool ContextOnly { get; set; }
    [JsonIgnore] public string DataUrl => string.IsNullOrEmpty(DataBase64) ? null : $"data:{MimeType};base64,{DataBase64}";
}

public static class ImageAttachmentContext
{
    public const int MaxImages = 4;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxAnalysisChars = 6000;

    public static string Describe(IEnumerable<ImageAttachment> images)
    {
        var sections = (images ?? Enumerable.Empty<ImageAttachment>())
            .Where(image => image != null && image.UseForGeneration).Take(MaxImages)
            .Select(image => $"Image ID: {image.Id}\nSource: {Limit(image.SourceLabel, 500)}\nSource link: {Limit(image.SourceUrl, 2000)}\nCaption: {Limit(image.Caption, 1000)}\n" +
                (string.IsNullOrWhiteSpace(image.Analysis) ? "Analysis status: unavailable" : Limit(image.Analysis, MaxAnalysisChars)))
            .ToArray();
        return sections.Length == 0 ? string.Empty :
            "SOURCE IMAGE CONTEXT:\n\n" + string.Join("\n\n", sections);
    }

    internal static string Limit(string value, int length) => string.IsNullOrEmpty(value) ? string.Empty : value.Substring(0, Math.Min(value.Length, length));
}
