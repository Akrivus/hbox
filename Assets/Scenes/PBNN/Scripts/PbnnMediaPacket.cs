using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Small, self-contained first slice. Large media can move to a file store later.
public sealed class PbnnMediaPacket
{
    public const string Key = "pbnn.media.v1";
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public int Version { get; set; } = 1;
    public string BulletinDate { get; set; }
    public List<PbnnMediaSegment> Segments { get; set; } = new List<PbnnMediaSegment>();

    public void Validate(int nodeCount)
    {
        if (Version != 1 || Segments == null || Segments.Count > 32)
            throw new ArgumentException("Unsupported or invalid PBNN media packet.");
        var end = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in Segments)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Id) || !ids.Add(segment.Id) ||
                segment.StartNode < end || segment.EndNodeExclusive <= segment.StartNode || segment.EndNodeExclusive > nodeCount)
                throw new ArgumentException("PBNN media segments need unique IDs and ordered, nonoverlapping node ranges.");
            end = segment.EndNodeExclusive;
            if (string.IsNullOrEmpty(segment.ImageBase64)) continue; // Explicit clear segment.
            if (segment.SurfaceId != "news-tv" || string.IsNullOrWhiteSpace(segment.Headline) ||
                string.IsNullOrWhiteSpace(segment.SourceId) || string.IsNullOrWhiteSpace(segment.SourceLabel))
                throw new ArgumentException("An image needs a known surface, headline and source attribution.");
            if (segment.ImageBase64.Length > ((MaxImageBytes + 2) / 3) * 4 ||
                Convert.FromBase64String(segment.ImageBase64).Length > MaxImageBytes)
                throw new ArgumentException("PBNN fixture images must be at most 2 MiB.");
        }
    }

    public PbnnMediaSegment AtNode(int index) => Segments.FirstOrDefault(s => index >= s.StartNode && index < s.EndNodeExclusive);

    public static PbnnMediaPacket Read(Dictionary<string, string> data, int nodeCount)
    {
        if (data == null || !data.TryGetValue(Key, out var json)) return null;
        // Bound the JSON before deserializing base64 strings.
        if (json == null || json.Length > 4 * MaxImageBytes) throw new ArgumentException("PBNN media packet is too large.");
        var packet = JsonConvert.DeserializeObject<PbnnMediaPacket>(json);
        if (packet == null) throw new ArgumentException("Empty PBNN media packet.");
        packet.Validate(nodeCount);
        return packet;
    }
}

public sealed class PbnnMediaSegment
{
    public string Id { get; set; }
    public int StartNode { get; set; }
    public int EndNodeExclusive { get; set; }
    public string SurfaceId { get; set; } = "news-tv";
    public string Headline { get; set; }
    public string SourceId { get; set; }
    public string SourceLabel { get; set; }
    public string SourceUrl { get; set; }
    public string Origin { get; set; }
    public string Caption { get; set; }
    public string ImageBase64 { get; set; }
}
