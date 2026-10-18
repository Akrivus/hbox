using System;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[CustomEditor(typeof(StudioPreview))]
public sealed class StudioPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "LocationIndex", "ShotIndex");
        serializedObject.ApplyModifiedProperties();
        var preview = (StudioPreview)target;
        var locations = preview.Context ? preview.Context.SpawnPoints : null;
        if (locations == null || locations.Length == 0)
        {
            EditorGUILayout.HelpBox("Assign a context with registered SpawnPoints.", MessageType.Info);
            return;
        }
        EditorGUI.BeginChangeCheck();
        int location = EditorGUILayout.Popup("Location", Mathf.Clamp(preview.LocationIndex, 0, locations.Length - 1),
            locations.Select(x => x ? x.name : "(missing location)").ToArray());
        var group = locations[location];
        var cameras = group ? group.virtualCameras : null;
        int shot = cameras != null && cameras.Length > 0
            ? EditorGUILayout.Popup("Shot", Mathf.Clamp(preview.ShotIndex, 0, cameras.Length - 1),
                cameras.Select(x => x ? x.name : "(missing camera)").ToArray()) : 0;
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(preview, "Select studio preview shot");
            preview.LocationIndex = location;
            preview.ShotIndex = shot;
            EditorUtility.SetDirty(preview);
        }
        if (GUILayout.Button("Fill location spawns from context cast") && group)
        {
            Undo.RecordObject(preview, "Fill preview cast");
            var actors = preview.Context.Actors?.Where(x => x).ToArray() ?? Array.Empty<Actor>();
            preview.Cast = Enumerable.Range(0, group.spawnPoints?.Length ?? 0).Select(i => new StudioPreview.CastSlot
            {
                SpawnIndex = i, Actor = actors.Length > 0 ? actors[i % actors.Length] : null
            }).ToArray();
            EditorUtility.SetDirty(preview);
        }
        if (GUILayout.Button("View selected shot in Scene view")) StudioPreviewSession.Frame(preview);
        EditorGUILayout.HelpBox("Edit mode only. Choose any Actor assets in Cast. Actors follow spawn references as you move them. Optional pose clips are sampled on temporary copies. Gameplay scripts, speech and IK behaviours do not run. Saving or entering Play stops the preview.", MessageType.Info);
    }

    [MenuItem("Tools/Studio/Add Preview to Selected Context")]
    private static void AddPreview()
    {
        var context = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<ChatManagerContext>() : null;
        if (!context) return;
        var preview = context.GetComponent<StudioPreview>() ?? Undo.AddComponent<StudioPreview>(context.gameObject);
        preview.Context = context;
        Selection.activeObject = preview;
    }
}

/// <summary>All preview side effects are confined to temporary actors or restored immediately after evaluating a shot.</summary>
[InitializeOnLoad]
internal static class StudioPreviewSession
{
    private static StudioPreview active;
    private static GameObject root;
    private static string signature;
    private static double nextUpdate;
    private static readonly List<GameObject> clones = new List<GameObject>();

    static StudioPreviewSession()
    {
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
        EditorApplication.playModeStateChanged += _ => Stop();
        EditorSceneManager.sceneSaving += (_, __) => Stop();
        EditorSceneManager.sceneClosing += (_, __) => Stop();
    }

    private static void Stop()
    {
        if (active) active.PreviewEnabled = false;
        Clear();
        active = null;
    }

    private static void Clear()
    {
        if (root) Object.DestroyImmediate(root);
        clones.Clear();
        signature = null;
    }

    private static void Update()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < nextUpdate) return;
        nextUpdate = EditorApplication.timeSinceStartup + 0.1;
        var requested = Object.FindObjectsByType<StudioPreview>(FindObjectsSortMode.None)
            .FirstOrDefault(x => x.isActiveAndEnabled && x.PreviewEnabled && x.Context &&
                !EditorUtility.IsPersistent(x) && x.gameObject.scene.IsValid() &&
                PrefabStageUtility.GetPrefabStage(x.gameObject) == null);
        if (requested != active) { Clear(); active = requested; }
        if (!active) { if (root) Clear(); return; }
        try
        {
            string current = EditorJsonUtility.ToJson(active);
            if (signature != current || !root)
            {
                Clear();
                root = new GameObject("Studio preview (temporary)");
                root.SetActive(false);
                root.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(root, active.gameObject.scene);
                foreach (var slot in active.Cast ?? Array.Empty<StudioPreview.CastSlot>())
                {
                    GameObject clone = null;
                    if (slot?.Actor && slot.Actor.Prefab)
                    {
                        clone = Object.Instantiate(slot.Actor.Prefab, root.transform);
                        // The inactive parent prevents ExecuteAlways/gameplay initialization before stripping.
                        foreach (var script in clone.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                        foreach (var audio in clone.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
                        foreach (var camera in clone.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(camera);
                        foreach (var listener in clone.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(listener);
                        foreach (var animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                        foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                        foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                        foreach (var transform in clone.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                        clone.name = slot.Actor.Name + " (preview)";
                    }
                    clones.Add(clone);
                }
                signature = current;
                root.SetActive(true);
            }
            var group = Group(active);
            for (int i = 0; i < clones.Count; i++)
            {
                var clone = clones[i];
                if (!clone) continue;
                var slot = active.Cast[i];
                var point = slot.SpawnOverride;
                if (!point && group && group.spawnPoints != null && slot.SpawnIndex >= 0 && slot.SpawnIndex < group.spawnPoints.Length)
                    point = group.spawnPoints[slot.SpawnIndex]?.transform;
                clone.SetActive(point);
                if (!point) continue;
                if (slot.Pose) slot.Pose.SampleAnimation(clone, Mathf.Clamp(slot.PoseTime, 0, slot.Pose.length));
                clone.transform.SetPositionAndRotation(point.position, point.rotation);
            }
            SceneView.RepaintAll();
        }
        catch (Exception exception) { Debug.LogException(exception); Stop(); }
    }

    private static SpawnPointManager Group(StudioPreview preview)
    {
        var groups = preview.Context ? preview.Context.SpawnPoints : null;
        return groups != null && groups.Length > 0 ? groups[Mathf.Clamp(preview.LocationIndex, 0, groups.Length - 1)] : null;
    }

    internal static void Frame(StudioPreview preview)
    {
        Update();
        var group = Group(preview);
        var cameras = group ? group.virtualCameras : null;
        if (cameras == null || cameras.Length == 0) return;
        var camera = cameras[Mathf.Clamp(preview.ShotIndex, 0, cameras.Length - 1)];
        if (!camera) return;
        var transforms = new[] { camera.transform, group.anchor, group.target, group.targetGroup ? group.targetGroup.transform : null }
            .Where(x => x).Distinct().ToArray();
        var positions = transforms.Select(x => x.position).ToArray();
        var rotations = transforms.Select(x => x.rotation).ToArray();
        var oldTargets = group.targetGroup ? group.targetGroup.m_Targets : null;
        try
        {
            var targets = active == preview ? clones.Where(x => x && x.activeSelf).Select(x => x.transform).ToArray() : Array.Empty<Transform>();
            if (targets.Length > 0)
            {
                var focus = targets[Mathf.Clamp(preview.FocusActorIndex, 0, targets.Length - 1)];
                if (group.anchor) group.anchor.SetPositionAndRotation(focus.position + Vector3.up * 1.5f, focus.rotation);
                if (group.target) group.target.position = focus.position + Vector3.up * 1.5f;
                if (group.targetGroup) group.targetGroup.m_Targets = targets.Select(x => new CinemachineTargetGroup.Target
                    { target = x, weight = 1, radius = 1.5f }).ToArray();
            }
            camera.PreviousStateIsValid = false;
            camera.UpdateCameraState(Vector3.up, -1);
            var state = camera.State;
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.cameraSettings.fieldOfView = state.Lens.FieldOfView;
            view.orthographic = state.Lens.Orthographic;
            view.LookAtDirect(state.FinalPosition + state.FinalOrientation * Vector3.forward * 5,
                state.FinalOrientation, state.Lens.Orthographic ? state.Lens.OrthographicSize :
                    5 * Mathf.Sin(state.Lens.FieldOfView * Mathf.Deg2Rad * 0.5f));
            view.Repaint();
        }
        finally
        {
            if (group.targetGroup) group.targetGroup.m_Targets = oldTargets;
            for (int i = 0; i < transforms.Length; i++) if (transforms[i]) transforms[i].SetPositionAndRotation(positions[i], rotations[i]);
            camera.PreviousStateIsValid = false;
        }
    }
}
