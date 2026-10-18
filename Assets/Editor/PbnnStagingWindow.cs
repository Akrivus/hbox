using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class PbnnStagingWindow : EditorWindow
{
    private Transform hostPosition;
    private CinemachineVirtualCamera fixedShot;

    [MenuItem("HBOx/PBNN/Configure Fixture Staging")]
    private static void Open() => GetWindow<PbnnStagingWindow>("PBNN Fixture Staging");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Place a transform at the host's feet in the new set and author a fixed camera framing that spot. This creates a one-host spawn group and registers it instead of the old office group. It does not move or delete your set objects.", MessageType.Info);
        hostPosition = (Transform)EditorGUILayout.ObjectField("Host position", hostPosition, typeof(Transform), true);
        fixedShot = (CinemachineVirtualCamera)EditorGUILayout.ObjectField("Fixed virtual camera", fixedShot, typeof(CinemachineVirtualCamera), true);
        using (new EditorGUI.DisabledScope(Application.isPlaying || hostPosition == null || fixedShot == null))
            if (GUILayout.Button("Create and register fixture staging")) Configure();
    }

    private void Configure()
    {
        var scene = hostPosition.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Use a host position in the open PBNN scene, not a prefab asset.");
            return;
        }
        ChatManagerContext context = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<ChatManagerContext>(true))
                if (candidate.Key == "pbnn") context = candidate;
        if (context == null || fixedShot.gameObject.scene != scene)
        {
            Debug.LogError("Choose a host position and camera from the PBNN scene.");
            return;
        }
        if (context.transform.Find("PBNN Fixture Staging") != null)
        {
            Debug.LogWarning("PBNN Fixture Staging already exists. Adjust its transform/spawn/camera in the Inspector, or Undo before creating it again.");
            return;
        }
        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure PBNN fixture staging");
        var group = new GameObject("PBNN Fixture Staging");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, scene);
        Undo.RegisterCreatedObjectUndo(group, "Create fixture staging");
        group.transform.SetParent(context.transform, false);
        group.transform.SetPositionAndRotation(hostPosition.position, hostPosition.rotation);
        Transform Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(group.transform, false);
            return go.transform;
        }
        var manager = group.AddComponent<SpawnPointManager>();
        manager.anchor = Child("Camera Anchor");
        manager.target = Child("Camera Target");
        manager.targetGroup = Child("Camera Target Group").gameObject.AddComponent<CinemachineTargetGroup>();
        manager.virtualCameras = new[] { fixedShot };
        manager.spawnPoints = new[] { new SpawnPointManager.SpawnPoint { transform = Child("Host"), stationary = true } };
        Undo.RecordObject(context, "Register fixture staging");
        context.SpawnPoints = new[] { manager };
        context.FallbackSpawnPoints = new Transform[0];
        EditorUtility.SetDirty(context);
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = group;
        Debug.Log("PBNN now registers one new-set fixture spawn group. Save the scene. The chosen camera should use an authored fixed transform; inspect its Follow/Aim components before testing.");
    }
}
