using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

internal static class MediaTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        catch (FormatException) { checks++; return; }
        throw new Exception(message);
    }
    public static void Main()
    {
        var bytes = File.ReadAllBytes("Assets/Scenes/PBNN/Fixtures/LocalImage.bytes");
        var packet = new PbnnMediaPacket
        {
            BulletinDate = "2026-10-07",
            Segments = new List<PbnnMediaSegment>
            {
                new PbnnMediaSegment { Id = "intro", StartNode = 0, EndNodeExclusive = 1 },
                new PbnnMediaSegment
                {
                    Id = "image", StartNode = 1, EndNodeExclusive = 3,
                    Headline = "Local fixture", SourceId = "local-1", SourceLabel = "Local illustration",
                    Origin = "uploaded", Caption = "Preserved caption", SourceUrl = "https://example.org/source",
                    ImageBase64 = Convert.ToBase64String(bytes)
                },
                new PbnnMediaSegment { Id = "close", StartNode = 3, EndNodeExclusive = 4 }
            }
        };
        packet.Validate(4);
        // Round-trip through the same nested GeneratedData string representation as a saved Chat.
        var saved = JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            ["GeneratedData"] = new Dictionary<string, string> { [PbnnMediaPacket.Key] = JsonConvert.SerializeObject(packet) }
        });
        var envelope = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(saved);
        var loaded = PbnnMediaPacket.Read(envelope["GeneratedData"], 4);
        Check(loaded.BulletinDate == packet.BulletinDate, "Bulletin date lost");
        Check(Convert.ToBase64String(bytes) == loaded.AtNode(1).ImageBase64, "Image bytes changed");
        Check(loaded.AtNode(2).SourceId == "local-1" && loaded.AtNode(2).Caption == "Preserved caption", "Source/caption lost");
        Check(loaded.AtNode(1).SourceUrl == "https://example.org/source", "Source URL lost");
        Check(loaded.AtNode(0).ImageBase64 == null, "Opening must clear");
        Check(loaded.AtNode(3).ImageBase64 == null, "Following segment must clear");
        Check(loaded.AtNode(-1) == null && loaded.AtNode(4) == null, "Outside boundaries must clear");
        Check(PbnnMediaPacket.Read(new Dictionary<string, string>(), 4) == null, "Old episodes must omit media");
        loaded.Version = 2;
        Reject(() => loaded.Validate(4), "Unknown version accepted");
        loaded.Version = 1;
        loaded.Segments[1].StartNode = 0;
        Reject(() => loaded.Validate(4), "Overlapping stories accepted");
        loaded.Segments[1].StartNode = 1;
        Reject(() => loaded.Validate(2), "Out-of-range node accepted");
        loaded.Segments[1].SurfaceId = "unknown";
        Reject(() => loaded.Validate(4), "Unknown display accepted");
        loaded.Segments[1].SurfaceId = "news-tv";
        loaded.Segments[1].SourceLabel = null;
        Reject(() => loaded.Validate(4), "Missing source accepted");
        loaded.Segments[1].SourceLabel = "Local illustration";
        loaded.Segments[1].ImageBase64 = "invalid image base64";
        Reject(() => loaded.Validate(4), "Invalid image encoding accepted");
        loaded.Segments[1].ImageBase64 = Convert.ToBase64String(new byte[PbnnMediaPacket.MaxImageBytes + 1]);
        Reject(() => loaded.Validate(4), "Oversized image accepted");
        Console.WriteLine($"PASS: {checks} PBNN media persistence, boundary and rejection checks.");
    }
}
