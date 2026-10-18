using System;
using UnityEngine;

/// <summary>Editor staging choices. Does not participate in Chat playback.</summary>
[AddComponentMenu("Studio/Studio Preview")]
public sealed class StudioPreview : MonoBehaviour
{
    public ChatManagerContext Context;
    [Tooltip("Preview in Edit mode. Temporary actors are removed before saving or entering Play mode.")]
    public bool PreviewEnabled;
    [HideInInspector] public int LocationIndex;
    [HideInInspector] public int ShotIndex;
    [Min(0)] public int FocusActorIndex;
    public CastSlot[] Cast = Array.Empty<CastSlot>();

    [Serializable]
    public sealed class CastSlot
    {
        public Actor Actor;
        [Min(0)] public int SpawnIndex;
        [Tooltip("Optional global placement reference, outside the location's spawn list.")]
        public Transform SpawnOverride;
        public AnimationClip Pose;
        [Min(0)] public float PoseTime;
    }

    private void Reset() => Context = GetComponentInParent<ChatManagerContext>();
}
