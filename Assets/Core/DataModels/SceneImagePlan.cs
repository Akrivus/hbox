using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public sealed class SceneImageCue
{
    public string ImageId { get; set; }
    public string SurfaceId { get; set; }
    public int StartNode { get; set; }
    public int EndNodeExclusive { get; set; }
    public string Caption { get; set; }
    public string Reason { get; set; }
}

public sealed class SceneImagePlan
{
    public const string Key = "scene.images.v1";
    public const int MaxJsonChars = 128 * 1024;
    public int Version { get; set; } = 1;
    public List<SceneImageCue> Cues { get; set; } = new List<SceneImageCue>();

    public static bool CanDisplay(ImageAttachment image) => image != null && !image.ContextOnly && image.PresentationEligible;

    public static ImageAttachment FindImage(IEnumerable<ImageAttachment> images, string id) =>
        images?.FirstOrDefault(image => image != null && image.Id == id);

    public static byte[] ReadBytes(ImageAttachment image)
    {
        if (image == null || string.IsNullOrEmpty(image.DataBase64) ||
            image.DataBase64.Length > ((ImageAttachmentContext.MaxImageBytes + 2) / 3) * 4)
            throw new ArgumentException("Missing or oversized scene image.");
        var bytes = Convert.FromBase64String(image.DataBase64);
        if (!ImageAttachmentService.TryReadImage(bytes, out _))
            throw new ArgumentException("Invalid scene image.");
        return bytes;
    }

    public void Validate(IEnumerable<ImageAttachment> images, int nodeCount, IEnumerable<string> surfaces = null, bool requireApproval = true)
    {
        if (Version != 1 || Cues == null || Cues.Count > 32)
            throw new ArgumentException("Invalid scene image plan version or cue count.");
        
        var assets = (images ?? Enumerable.Empty<ImageAttachment>()).Where(image => image != null).ToArray();
        if (assets.GroupBy(image => image.Id).Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1))
            throw new ArgumentException("Scene images require unique attachment IDs.");
        
        var allowed = surfaces == null ? null : new HashSet<string>(surfaces, StringComparer.Ordinal);
        var ends = new Dictionary<string, int>();
        foreach (var cue in Cues.OrderBy(cue => cue?.StartNode ?? -1))
        {
            if (cue == null || string.IsNullOrWhiteSpace(cue.SurfaceId) || (allowed != null && !allowed.Contains(cue.SurfaceId)) ||
                cue.StartNode < 0 || cue.EndNodeExclusive <= cue.StartNode || cue.EndNodeExclusive > nodeCount ||
                (ends.TryGetValue(cue.SurfaceId, out var end) && cue.StartNode < end) ||
                string.IsNullOrWhiteSpace(cue.Caption) || cue.Caption.Length > 300 || string.IsNullOrWhiteSpace(cue.Reason) || cue.Reason.Length > 1000)
                throw new ArgumentException("Scene image cues need known surfaces, valid non-overlapping ranges, captions and selection reasons.");
            ends[cue.SurfaceId] = cue.EndNodeExclusive;
            
            var image = FindImage(assets, cue.ImageId);
            if (image == null || image.ContextOnly || (requireApproval && !image.PresentationEligible) ||
                string.IsNullOrWhiteSpace(image.Analysis) || string.IsNullOrWhiteSpace(image.SourceLabel))
                throw new ArgumentException("A scene cue references an unavailable or context-only image.");
            ReadBytes(image);
        }
    }

    public SceneImageCue AtNode(string surface, int index) =>
        Cues.FirstOrDefault(cue => cue.SurfaceId == surface && index >= cue.StartNode && index < cue.EndNodeExclusive);

    private static SceneImagePlan Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonChars)
            throw new ArgumentException("Missing or oversized scene image plan.");
        return JsonConvert.DeserializeObject<SceneImagePlan>(json) ?? throw new ArgumentException("Empty scene image plan.");
    }

    public static SceneImagePlan Approve(string json, IList<ImageAttachment> images, int nodeCount, IEnumerable<string> surfaces)
    {
        foreach (var image in images.Where(image => image != null))
            image.PresentationEligible = false;
        var plan = Parse(json);
        plan.Validate(images, nodeCount, surfaces, requireApproval: false);
        foreach (var cue in plan.Cues)
            FindImage(images, cue.ImageId).PresentationEligible = true;
        return plan;
    }

    public static SceneImagePlan Read(Dictionary<string, string> data, IEnumerable<ImageAttachment> images, int nodeCount)
    {
        if (data == null || !data.TryGetValue(Key, out var json))
            return null;
        var plan = Parse(json);
        plan.Validate(images, nodeCount);
        return plan;
    }
}
