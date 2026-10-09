# Synty mesh transfer

The older `Assets/Core/BoneHacker.cs` component still exists. It manually replaces a SkinnedMeshRenderer bone array; it does not generate a mapping or replace the root bone.

Use **HBOx → Actors → Synty Bone Transfer** for the editor workflow:

1. Open a humanoid prefab variant in Prefab Mode, or work on a scene instance.
2. Copy the desired model's mesh object into that character hierarchy. Preserve its SkinnedMeshRenderer, mesh, materials and transform alignment. Keep the original source skeleton available until remapping is finished; its bone references provide the mapping input.
3. Multi-select the imported mesh objects in the Hierarchy, then click **Use selected renderers (including children)**. Selecting their imported parent also collects its inactive child renderers. Select only the imported objects, not the whole destination character. Alternatively, add renderer slots manually. Duplicate renderers are processed once. Select the destination skeleton's **Root** bone for this office-pack humanoid, rather than Hips or the character's top-level container. The source root is detected separately from each renderer's existing `rootBone`.
4. Preview the batch mapping. Relative bone paths are preferred; a unique bone-name match is the fallback. Missing, ambiguous, null or out-of-root bones block the entire batch. Empty renderer slots must be removed or assigned. Source bone order is preserved because mesh skin weights index that array. Every renderer is validated before any changes are applied.
5. Apply, then inspect the rest pose, talk animation and IK deformation. Check renderer bounds/culling, scale and mesh placement. One Undo restores the whole batch. Save the prefab only after inspection. Keep the old source skeleton until all imported renderers are remapped; move the mesh objects out of its hierarchy while preserving their transforms before removing that skeleton.

The tool changes only `bones` and `rootBone` on the listed renderers. It retains the destination skeleton, Animator and IK components, and does not modify either rig hierarchy, the meshes' bind poses, materials, costume-controller lists or actor ScriptableObjects. Add new costume renderers to the appropriate costume list separately.

Compatible names do not guarantee compatible bind poses, proportions or coordinate conventions across packs. This is a bone-reference remapper, not a general rig converter. If the imported model distorts, undo and inspect the source/target rest poses and mesh alignment before proceeding. Persistent prefab assets must be opened in Prefab Mode; applying during Play mode is blocked. The tool does not save assets automatically.

Validation: batch editor code compiled against Unity 6000.3.10f1 modules with zero warnings/errors. Visual deformation and grouped Undo behavior still require a Unity test with an actual mesh transfer.
