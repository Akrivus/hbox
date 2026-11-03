using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SceneImageValidation
{
    [MenuItem("HBOx/Images/Validate Scene Image Bindings")]
    public static void Run()
    {
        var shader = Resources.Load<Shader>("SceneImageUI");
        Require(shader != null, "Missing Resources/SceneImageUI shader");
        var messages = ShaderUtil.GetShaderMessages(shader);
        Require(!messages.Any(message => message.severity.ToString() == "Error"), "Image UI shader errors: " + string.Join("; ", messages.Select(message => message.message)));
        foreach (var entry in new[] { ("Assets/Scenes/PBNN.unity", "pbnn", "news-tv"), ("Assets/Scenes/polbots.unity", "polbots", "media-screen") })
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(entry.Item1);
            var opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(entry.Item1, OpenSceneMode.Additive);
            try
            {
                var context = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ChatManagerContext>(true)).Single(owner => owner.Key == entry.Item2);
                Require(context.GetComponent<SceneImagePresentation>() != null, "Missing authored scene-image controller");
                var surfaces = SceneImagePresentation.InScene<MonoBehaviour>(context).OfType<ISceneImageSurface>().Where(surface => surface.IsConfigured).ToArray();
                Require(surfaces.Any(surface => surface.SurfaceId == entry.Item3), "Missing authored display binding: " + entry.Item3);
                Require(surfaces.GroupBy(surface => surface.SurfaceId).All(group => group.Count() == 1), "Duplicate authored image surface IDs");
                Debug.Log("Scene image binding PASS: " + entry.Item2 + " | " + string.Join(", ", surfaces.Select(surface => surface.SurfaceId)));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        Debug.Log("Scene image validation PASS: resource shader and authored TV/media-screen bindings. Visual playback still needs a Play-mode check.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
