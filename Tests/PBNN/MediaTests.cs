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
        catch (JsonException) { checks++; return; }
        throw new Exception(message);
    }
    private static void SceneImages(byte[] bytes)
    {
        var images = new List<ImageAttachment>
        {
            new ImageAttachment { Id = "photo", Analysis = "An attributed scene photograph", SourceLabel = "Source: scene photo", DataBase64 = Convert.ToBase64String(bytes) },
            new ImageAttachment { Id = "private", Analysis = "Context screenshot", SourceLabel = "Source: context", DataBase64 = Convert.ToBase64String(bytes), ContextOnly = true }
        };
        var surfaces = new[] { "news-tv", "room-display" };
        var json = @"{'Version':1,'Cues':[{'ImageId':'photo','SurfaceId':'news-tv','StartNode':1,'EndNodeExclusive':3,'Caption':'Scene photo','Reason':'Matches the report'},{'ImageId':'photo','SurfaceId':'room-display','StartNode':1,'EndNodeExclusive':2,'Caption':'Scene photo','Reason':'Room detail'}]}";
        var plan = SceneImagePlan.Approve(json, images, 5, surfaces);
        Check(images[0].PresentationEligible && !images[1].PresentationEligible, "only scheduled attachments become display eligible");
        Check(plan.AtNode("news-tv", 0) == null && plan.AtNode("news-tv", 1) != null && plan.AtNode("news-tv", 2) != null && plan.AtNode("news-tv", 3) == null, "shared plan uses inclusive start/exclusive end and clears gaps");
        Check(plan.AtNode("room-display", 1) != null && plan.AtNode("room-display", 2) == null, "different surfaces can display simultaneously with independent boundaries");
        var saved = JsonConvert.SerializeObject(new Dictionary<string, object> { ["Images"] = images, ["GeneratedData"] = new Dictionary<string, string> { [SceneImagePlan.Key] = JsonConvert.SerializeObject(plan) } });
        var envelope = Newtonsoft.Json.Linq.JObject.Parse(saved);
        var replayImages = envelope["Images"].ToObject<List<ImageAttachment>>();
        var replay = SceneImagePlan.Read(envelope["GeneratedData"].ToObject<Dictionary<string, string>>(), replayImages, 5);
        Check(replay.AtNode("news-tv", 1).ImageId == "photo" && SceneImagePlan.ReadBytes(replayImages[0]).Length == bytes.Length, "shared plan and exact asset survive replay serialization");
        Check(!JsonConvert.SerializeObject(plan).Contains(images[0].DataBase64), "plan references existing assets instead of duplicating image bytes");
        replayImages[0].ContextOnly = true;
        Check(!SceneImagePlan.CanDisplay(replayImages[0]), "hard context-only veto wins over persisted approval");
        Reject(() => replay.Validate(replayImages, 5), "replay must reject a newly context-only asset");
        Reject(() => SceneImagePlan.Approve(json.Replace("'photo'", "'private'"), images, 5, surfaces), "prompt cannot override context-only flag");
        Check(!images[0].PresentationEligible && !images[1].PresentationEligible, "invalid selection clears previous eligibility");
        Reject(() => SceneImagePlan.Approve(json.Replace("news-tv", "invented"), images, 5, surfaces), "invented display surface rejected");
        Reject(() => SceneImagePlan.Approve(json.Replace("'photo'", "'invented'"), images, 5, surfaces), "invented attachment rejected");
        Reject(() => SceneImagePlan.Approve(json.Replace("'EndNodeExclusive':3", "'EndNodeExclusive':9"), images, 5, surfaces), "outside-node range rejected");
        Reject(() => SceneImagePlan.Approve(json.Replace("room-display", "news-tv"), images, 5, surfaces), "same-surface overlap rejected");
        Reject(() => SceneImagePlan.Approve(json.Replace("'Version':1", "'Version':2"), images, 5, surfaces), "unsupported shared schema rejected");
        Reject(() => SceneImagePlan.Approve("not JSON", images, 5, surfaces), "malformed model output rejected");
        Reject(() => SceneImagePlan.Approve(new string('x', SceneImagePlan.MaxJsonChars + 1), images, 5, surfaces), "oversized model output rejected");
        var duplicate = new List<ImageAttachment> { images[0], images[0] };
        Reject(() => SceneImagePlan.Approve(json, duplicate, 5, surfaces), "ambiguous attachment ID rejected");
        images[0].Analysis = null;
        Reject(() => SceneImagePlan.Approve(json, images, 5, surfaces), "unanalyzed asset cannot air");
        images[0].Analysis = "Photo"; images[0].DataBase64 = "invalid";
        Reject(() => SceneImagePlan.Approve(json, images, 5, surfaces), "invalid replay bytes cannot air");
        var empty = SceneImagePlan.Approve("{'Version':1,'Cues':[]}", images, 5, surfaces);
        Check(empty.Cues.Count == 0 && !images[0].PresentationEligible, "explicit no-air decision is valid and keeps assets off air");
        Check(SceneImagePlan.Read(new Dictionary<string, string>(), images, 5) == null, "old episodes without shared plans remain compatible");
    }

    public static void Main()
    {
        var bytes = File.ReadAllBytes("Assets/Scenes/PBNN/Fixtures/LocalImage.bytes");
        SceneImages(bytes);
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
