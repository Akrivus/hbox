using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Video-call media policy belongs to this scene, including soccer's display priority.
public sealed class PolbotsImagePresentation : MonoBehaviour, ISceneImageSurface
{
    public string SurfaceId = "media-screen";
    public string Description = "Video-call media screen";
    public ShareScreenUIManager Media;
    private SceneImagePresentation presentation;
    private Material material;
    public bool IsConfigured
    {
        get
        {
            if (this == null || !enabled) return false;
            ResolveMedia();
            return Media != null && Media.HasMediaSurface;
        }
    }
    public bool IsShowing => material != null;
    string ISceneImageSurface.SurfaceId => SurfaceId;
    string ISceneImageSurface.Description => Description;

    private void OnEnable()
    {
        presentation = GetComponent<SceneImagePresentation>();
        if (presentation != null) presentation.OnCollectSurfaces += CollectSurface;
    }
    private void CollectSurface(ICollection<ISceneImageSurface> surfaces) => surfaces.Add(this);
    private void ResolveMedia()
    {
        var owner = GetComponent<ChatManagerContext>();
        if (Media == null && owner != null) Media = SceneImagePresentation.InScene<ShareScreenUIManager>(owner).FirstOrDefault();
    }
    public bool CanDisplay(Chat chat) => IsConfigured && !SoccerGameSource.IsSoccerMode(chat) &&
        (SoccerGameSource.Instance == null || (!SoccerGameSource.Instance.IsGameLoaded && !SoccerGameSource.Instance.IsSceneLoaded));

    public void Show(Texture2D texture, SceneImageCue cue, ImageAttachment image)
    {
        var shader = Resources.Load<Shader>("SceneImageUI");
        if (shader == null) throw new InvalidOperationException("Missing scene image UI shader.");
        material = new Material(shader);
        material.SetFloat("_ImageAspect", (float)texture.width / texture.height);
        var size = Media.ActiveMediaScreenSize;
        material.SetFloat("_ScreenAspect", size.y > 0 ? size.x / size.y : 16f / 9f);
        if (!Media.TryShowAttachment(this, texture, material)) Clear();
    }
    public void Clear()
    {
        if (Media != null) Media.ReleaseAttachment(this);
        if (material != null) { Destroy(material); material = null; }
    }
    private void OnDisable()
    {
        if (presentation != null) presentation.OnCollectSurfaces -= CollectSurface;
        presentation = null;
        Clear();
    }
}
