using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PbnnMediaValidation
{
    // Also callable from a headless editor with -executeMethod PbnnMediaValidation.Run.
    [MenuItem("HBOx/PBNN/Validate Media Scene")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Validate outside Play mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/PBNN.unity");
        var opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/PBNN.unity", OpenSceneMode.Additive);
        Texture2D decoded = null;
        try
        {
            var roots = scene.GetRootGameObjects();
            var presenter = roots.SelectMany(r => r.GetComponentsInChildren<PbnnMediaPresentation>(true)).Single();
            var fixture = roots.SelectMany(r => r.GetComponentsInChildren<PbnnMediaFixtureGeneration>(true)).Single();
            var context = presenter.GetComponent<ChatManagerContext>();
            Require(context != null && context.SpawnPoints != null, "Missing PBNN staging registration");
            foreach (var location in context.SpawnPoints)
            {
                Require(location != null && location.IsReady && location.virtualCameras.All(c => c != null), "A registered location has missing camera/target references");
                Require(location.spawnPoints != null && location.spawnPoints.Length > 0 && location.spawnPoints.All(s => s != null && s.transform != null), "A registered location has missing host spawn references");
            }
            var desk = context.SpawnPoints.Single(s => s.name == "Desk");
            var host = desk.spawnPoints.Single(s => s.name == "Business Man 1");
            Require(host.stationary, "Fixture host must remain at its authored desk position");
            Require(desk.virtualCameras.Length == 1 && desk.virtualCameras[0].name == "Wide", "Desk fixture must use the existing Wide camera deterministically");
            Require(desk.virtualCameras[0].Follow == null && desk.virtualCameras[0].LookAt == null, "Fixture camera must retain fixed framing");
            Require(presenter.TvRenderer != null && presenter.ScreenShader != null, "Missing TV/shader scene reference");
            Require(presenter.TvRenderer.transform.IsChildOf(desk.transform.parent), "Media display must target the desk TV");
            Require(presenter.ScreenMaterialSlot < presenter.TvRenderer.sharedMaterials.Length, "Invalid screen material slot");
            Require(fixture.ImageBytes != null, "Missing local image fixture");
            var stages = fixture.GetComponents<ISubGenerator>();
            Require(ReferenceEquals(stages.Last(), fixture), "Fixture must be the final generation stage");
            var fixtureHost = new ActorContext(context.Actors.Single(a => a != null && a.Name == "Business Man 1"));
            var stagingProbe = new Chat
            {
                Key = "pbnn", ManagerContext = context,
                Idea = new Idea(PbnnMediaFixtureGeneration.StudioMarker),
                Actors = new[] { fixtureHost }, Nodes = new List<ChatNode>()
            };
            fixture.Generate(null, stagingProbe).GetAwaiter().GetResult();
            Require(stagingProbe.Location == "Desk" && fixtureHost.SpawnPoint == "Business Man 1", "Fixture generation must persist desk/host staging");
            Require(!ShaderUtil.ShaderHasError(presenter.ScreenShader), "Screen shader has compilation errors");
            var packet = new PbnnMediaPacket
            {
                BulletinDate = "2026-10-07",
                Segments = new List<PbnnMediaSegment>
                {
                    new PbnnMediaSegment
                    {
                        Id = "image", StartNode = 0, EndNodeExclusive = 1,
                        Headline = "Test", SourceId = "local", SourceLabel = "Local fixture",
                        ImageBase64 = Convert.ToBase64String(fixture.ImageBytes.bytes)
                    }
                }
            };
            var chat = new Chat { Key = "pbnn", Nodes = new List<ChatNode> { new ChatNode() } };
            chat.GeneratedData[PbnnMediaPacket.Key] = JsonConvert.SerializeObject(packet);
            var roundTrip = JsonConvert.DeserializeObject<Chat>(JsonConvert.SerializeObject(chat));
            var loaded = PbnnMediaPacket.Read(roundTrip.GeneratedData, roundTrip.Nodes.Count);
            decoded = new Texture2D(2, 2);
            Require(decoded.LoadImage(Convert.FromBase64String(loaded.AtNode(0).ImageBase64)), "Saved image did not decode");
            Require(decoded.width <= 4096 && decoded.height <= 4096, "Fixture image exceeds dimension limit");
            var mesh = presenter.TvRenderer.GetComponent<MeshFilter>().sharedMesh;
            Require(mesh != null && mesh.subMeshCount > presenter.ScreenMaterialSlot, "Missing TV screen submesh");
            var uv = mesh.uv;
            var selectedUv = mesh.GetIndices(presenter.ScreenMaterialSlot).Select(i => uv[i]).ToArray();
            Debug.Log($"PBNN media validation PASS: actual Chat JSON round-trip, {decoded.width}x{decoded.height} local image, scene references, final generator stage and shader. TV {presenter.TvRenderer.name}, slot {presenter.ScreenMaterialSlot}, screen UV bounds {selectedUv.Min(v => v.x)}..{selectedUv.Max(v => v.x)} / {selectedUv.Min(v => v.y)}..{selectedUv.Max(v => v.y)}. Playback/skip/exit still require the Play-mode sweep.");
        }
        finally
        {
            if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
