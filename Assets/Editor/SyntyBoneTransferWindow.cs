using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class SyntyBoneTransferWindow : EditorWindow
{
    [SerializeField] private List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
    private Transform targetRoot;
    private Vector2 scroll;
    private string report = "Select copied mesh renderers and the destination skeleton root (Root for the office-pack humanoid).";

    [MenuItem("HBOx/Actors/Synty Bone Transfer")]
    private static void Open() => GetWindow<SyntyBoneTransferWindow>("Synty Bone Transfer");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Copy the desired mesh object into your humanoid prefab first. This tool remaps that renderer onto the existing skeleton; it preserves the mesh, materials, bone order and bind poses. Use a scene instance or Prefab Mode.", MessageType.Info);
        if (GUILayout.Button("Use selected renderers (including children)"))
            renderers = Selection.gameObjects.SelectMany(item => item.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Distinct().ToList();
        for (var i = 0; i < renderers.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            renderers[i] = (SkinnedMeshRenderer)EditorGUILayout.ObjectField($"Mesh {i + 1}", renderers[i], typeof(SkinnedMeshRenderer), true);
            if (GUILayout.Button("Remove", GUILayout.Width(65)))
            {
                renderers.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Add renderer"))
            renderers.Add(null);
        targetRoot = (Transform)EditorGUILayout.ObjectField("Target skeleton root", targetRoot, typeof(Transform), true);
        using (new EditorGUI.DisabledScope(renderers.Count == 0 || targetRoot == null || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Preview mapping"))
                TryMapBatch(out _);
            if (GUILayout.Button("Validate and apply (Undo supported)"))
                Apply();
        }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private bool TryMap(SkinnedMeshRenderer renderer, out Transform[] mapped, out Transform mappedRoot)
    {
        mapped = null;
        mappedRoot = null;
        var notes = new List<string>();
        if (renderer == null || targetRoot == null || renderer.sharedMesh == null || renderer.rootBone == null)
        {
            report = "A renderer with a mesh and source root bone, and a target skeleton root, are required.";
            return false;
        }
        var sourceRoot = renderer.rootBone;
        var bones = renderer.bones;
        if (bones.Length == 0 || renderer.sharedMesh.bindposes.Length != bones.Length)
        {
            report = "Source bone count must be nonzero and match the mesh bind-pose count.";
            return false;
        }

        // Relative paths disambiguate common repeated names such as fingers.
        // A unique name fallback accommodates compatible Synty hierarchy variations.
        var targets = targetRoot.GetComponentsInChildren<Transform>(true);
        var byPath = targets.GroupBy(t => RelativePath(t, targetRoot))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var byName = targets.GroupBy(t => t.name)
            .ToDictionary(g => g.Key, g => g.ToArray());
        mapped = new Transform[bones.Length];
        mappedRoot = targetRoot;
        var valid = true;
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            if (bone == null || (bone != sourceRoot && !bone.IsChildOf(sourceRoot)))
            {
                notes.Add($"ERROR [{i}]: source bone is null or outside the source root.");
                valid = false;
                continue;
            }
            var path = RelativePath(bone, sourceRoot);
            Transform[] matches;
            var pathMatch = byPath.TryGetValue(path, out matches) && matches.Length == 1;
            if (!pathMatch && (!byName.TryGetValue(bone.name, out matches) || matches.Length != 1))
            {
                notes.Add($"ERROR [{i}] {path}: missing or ambiguous destination bone.");
                valid = false;
                continue;
            }
            mapped[i] = matches[0];
            notes.Add($"[{i}] {path} -> {RelativePath(mapped[i], targetRoot)}{(pathMatch ? "" : " (unique name fallback)")}");
        }
        if (mapped.Where(t => t != null).Distinct().Count() != mapped.Count(t => t != null))
        {
            notes.Add("ERROR: multiple source bones resolve to the same destination bone.");
            valid = false;
        }
        report = (valid ? "Mapping complete. " : "Mapping blocked. ") +
            "Matching names do not prove compatible bind poses or proportions. Preview deformation in the rest pose and a talk/IK animation before saving.\n\n" + string.Join("\n", notes);
        return valid;
    }

    private void Apply()
    {
        if (EditorApplication.isPlaying)
        {
            report = "Apply to a scene instance or a renderer opened in Prefab Mode, outside Play mode.";
            return;
        }
        if (!TryMapBatch(out var mappings))
            return;
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Transfer Synty mesh bones (batch)");
        Undo.RecordObjects(mappings.Select(item => (UnityEngine.Object)item.Renderer).ToArray(), "Transfer Synty mesh bones (batch)");
        try
        {
            foreach (var mapping in mappings)
            {
                mapping.Renderer.bones = mapping.Bones;
                mapping.Renderer.rootBone = mapping.Root;
                PrefabUtility.RecordPrefabInstancePropertyModifications(mapping.Renderer);
                EditorUtility.SetDirty(mapping.Renderer);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            report = $"Applied {mappings.Count} renderers. Inspect rest pose, animation and bounds before saving. One Undo restores the batch.\n\n" + report;
        }
        catch (Exception exception)
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
            report = "Batch reverted after an apply error: " + exception.Message;
            Debug.LogException(exception);
        }
    }

    private sealed class Mapping
    {
        public SkinnedMeshRenderer Renderer;
        public Transform[] Bones;
        public Transform Root;
    }

    private bool TryMapBatch(out List<Mapping> mappings)
    {
        mappings = new List<Mapping>();
        var reports = new List<string>();
        var valid = renderers.Count > 0 && targetRoot != null;
        if (!valid)
        {
            report = "Add at least one renderer and select the target skeleton root.";
            return false;
        }
        foreach (var renderer in renderers.Distinct())
        {
            if (renderer == null)
            {
                reports.Add("ERROR: an empty renderer slot must be assigned or removed.");
                valid = false;
                continue;
            }
            if (EditorUtility.IsPersistent(renderer) || EditorUtility.IsPersistent(targetRoot) || renderer.transform.root != targetRoot.root)
            {
                reports.Add($"ERROR {renderer.name}: use a scene instance or Prefab Mode and copy the mesh under the target character hierarchy.");
                valid = false;
                continue;
            }
            var success = TryMap(renderer, out var bones, out var root);
            reports.Add($"--- {renderer.name} ---\n{report}");
            valid &= success;
            if (success)
                mappings.Add(new Mapping { Renderer = renderer, Bones = bones, Root = root });
        }
        report = (valid ? $"All {mappings.Count} renderers validated.\n\n" : "Batch blocked; no renderers changed.\n\n") + string.Join("\n\n", reports);
        return valid;
    }

    private static string RelativePath(Transform bone, Transform root)
    {
        var parts = new List<string>();
        for (var current = bone; current != root; current = current.parent)
        {
            if (current == null)
                throw new ArgumentException("Bone is outside the skeleton root.");
            parts.Add(current.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
