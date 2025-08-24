using UnityEngine;

[ExecuteAlways]
public class BoneHacker : MonoBehaviour
{
    public SkinnedMeshRenderer SkinnedMeshRenderer;

    public Transform[] oldBones;
    public Transform[] newBones;
    public bool compile;

    void Update()
    {
        oldBones = SkinnedMeshRenderer.bones;
        if (compile)
            SkinnedMeshRenderer.bones = newBones;
        compile = false;
    }
}
