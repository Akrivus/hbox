using UnityEngine;

// Scene-owned displays register through SceneImagePresentation.OnCollectSurfaces.
public interface ISceneImageSurface
{
    string SurfaceId { get; }
    string Description { get; }
    bool IsConfigured { get; }
    bool IsShowing { get; }
    bool CanDisplay(Chat chat);
    void Show(Texture2D texture, SceneImageCue cue, ImageAttachment image);
    void Clear();
}
