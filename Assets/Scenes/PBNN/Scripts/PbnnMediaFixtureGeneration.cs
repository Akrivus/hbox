using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public sealed class PbnnMediaFixtureGeneration : MonoBehaviour, ISubGenerator
{
    public const string Marker = "[PBNN_MEDIA_FIXTURE]";
    public const string StudioMarker = "[PBNN_STUDIO_FIXTURE]";
    public TextAsset ImageBytes;
    public bool IsBlocking => true;

    public Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        if (chat.ManagerContext?.Key != "pbnn")
            return Task.FromResult(chat);
        var mediaFixture = chat.Idea?.Prompt?.Contains(Marker) ?? false;
        if (!mediaFixture && !(chat.Idea?.Prompt?.Contains(StudioMarker) ?? false)) return Task.FromResult(chat);
        // Override model-selected staging only for the explicit fixture commands.
        chat.Location = "Desk";
        foreach (var actor in chat.Actors)
            if (actor.Name == "Business Man 1") actor.SpawnPoint = "Business Man 1";
        if (!mediaFixture) return Task.FromResult(chat);
        if (ImageBytes == null || ImageBytes.bytes.Length == 0 || ImageBytes.bytes.Length > PbnnMediaPacket.MaxImageBytes || chat.Nodes.Count < 4)
            throw new InvalidOperationException("Media fixture requires a local image (up to 2 MiB) and at least four dialogue nodes.");
        var packet = new PbnnMediaPacket
        {
            BulletinDate = "2026-10-07",
            Segments = new List<PbnnMediaSegment>
            {
                new PbnnMediaSegment { Id = "opening", StartNode = 0, EndNodeExclusive = 1 },
                new PbnnMediaSegment
                {
                    Id = "local-image", StartNode = 1, EndNodeExclusive = chat.Nodes.Count - 1,
                    Headline = "PBNN studio image test", SourceId = "pbnn-local-fixture",
                    SourceLabel = "Source: local image fixture | Illustration, not match footage",
                    Origin = "local-fixture", Caption = "A local display test; not evidence of the reported match.",
                    ImageBase64 = Convert.ToBase64String(ImageBytes.bytes)
                },
                new PbnnMediaSegment { Id = "closing", StartNode = chat.Nodes.Count - 1, EndNodeExclusive = chat.Nodes.Count }
            }
        };
        packet.Validate(chat.Nodes.Count);
        if (chat.GeneratedData == null) chat.GeneratedData = new Dictionary<string, string>();
        chat.GeneratedData[PbnnMediaPacket.Key] = JsonConvert.SerializeObject(packet);
        return Task.FromResult(chat);
    }
}
