using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Add to authored image mounts, room displays or UI panels. IDs are the prompt/replay contract.
public sealed class SceneImageSurface : MonoBehaviour, ISceneImageSurface
{
    public string SurfaceId;
    public string Description;
    public Renderer TargetRenderer;
    public int MaterialSlot;
    public RawImage TargetImage;
    public Shader ScreenShader;
    public float ScreenAspect = 16f / 9f;
    public TMP_Text Caption;
    public TMP_Text Attribution;
    private Material material;
    private Material[] originals;
    private Material originalUiMaterial;
    private Texture originalUiTexture;
    private string originalCaption, originalAttribution;
    private bool originalCaptionRichText, originalAttributionRichText;

    public bool IsConfigured => enabled && !string.IsNullOrWhiteSpace(SurfaceId) &&
        ((TargetRenderer != null && ScreenShader != null && MaterialSlot >= 0 && MaterialSlot < TargetRenderer.sharedMaterials.Length) || TargetImage != null);
    public bool IsShowing => material != null;
    string ISceneImageSurface.SurfaceId => SurfaceId;
    string ISceneImageSurface.Description => Description;
    public bool CanDisplay(Chat chat) => IsConfigured;

    public void Show(Texture2D texture, SceneImageCue cue, ImageAttachment image)
    {
        if (!IsConfigured)
            return;
        if (material == null)
        {
            var shader = TargetImage != null ? Resources.Load<Shader>("SceneImageUI") : ScreenShader;
            if (shader == null) throw new System.InvalidOperationException("Missing scene image shader.");
            material = new Material(shader) { name = "Scene image: " + SurfaceId };
            if (TargetImage != null)
            {
                originalUiMaterial = TargetImage.material;
                originalUiTexture = TargetImage.texture;
                TargetImage.material = material;
            }
            else
            {
                originals = TargetRenderer.sharedMaterials;
                var slots = (Material[])originals.Clone(); slots[MaterialSlot] = material;
                TargetRenderer.sharedMaterials = slots;
            }
            if (Caption != null)
            {
                originalCaption = Caption.text;
                originalCaptionRichText = Caption.richText;
            }
            if (Attribution != null)
            {
                originalAttribution = Attribution.text;
                originalAttributionRichText = Attribution.richText;
            }
        }
        material.mainTexture = texture;
        material.SetFloat("_ImageAspect", (float)texture.width / texture.height);
        var rect = TargetImage != null ? TargetImage.rectTransform.rect : default;
        material.SetFloat("_ScreenAspect", rect.height > 0 ? rect.width / rect.height : ScreenAspect);
        if (TargetImage != null)
            TargetImage.texture = texture;
        if (Caption != null)
        {
            Caption.richText = false;
            Caption.text = cue.Caption;
        }
        if (Attribution != null)
        {
            Attribution.richText = false;
            Attribution.text = image.SourceLabel;
        }
    }

    public void Clear()
    {
        if (material == null)
            return;
        if (TargetRenderer != null && originals != null)
            TargetRenderer.sharedMaterials = originals;
        if (TargetImage != null)
        {
            TargetImage.material = originalUiMaterial;
            TargetImage.texture = originalUiTexture;
        }
        if (Caption != null)
        {
            Caption.text = originalCaption;
            Caption.richText = originalCaptionRichText;
        }
        if (Attribution != null)
        {
            Attribution.text = originalAttribution;
            Attribution.richText = originalAttributionRichText;
        }
        originals = null;
        Destroy(material);
        material = null;
    }

    private void OnDisable() => Clear();
}
