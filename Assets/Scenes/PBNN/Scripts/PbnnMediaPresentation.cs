using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PbnnMediaPresentation : MonoBehaviour
{
    public Renderer TvRenderer;
    public int ScreenMaterialSlot = 1;
    public Shader ScreenShader;
    public float ScreenAspect = 16f / 9f;

    private ChatManager manager;
    private ChatManagerContext owner;
    private Chat episode;
    private PbnnMediaPacket packet;
    private PbnnMediaSegment shown;
    private Texture2D texture;
    private Material screen;
    private Material[] originalMaterials;
    private GameObject overlay;
    private TMP_Text headline;
    private TMP_Text source;

    private void Start()
    {
        owner = GetComponent<ChatManagerContext>();
        Bind();
    }

    private void Bind()
    {
        if (manager != null || ChatManager.Instance == null) return;
        manager = ChatManager.Instance;
        manager.OnChatLoaded += Load;
        manager.OnChatNodeActivated += Activate;
        manager.OnContextChanged += ContextChanged;
    }

    private bool OwnsPlayback => manager != null && owner != null &&
        ChatManagerContext.Current == owner && episode != null &&
        manager.NowPlaying == episode && episode.Key == "pbnn" && !ChatManager.SkipToEnd;

    private void Update()
    {
        Bind();
        // Also covers natural completion, skip during speech, scene teardown and aborted playback.
        if (!OwnsPlayback) Clear();
    }

    private void Load(Chat chat)
    {
        Clear();
        episode = null;
        packet = null;
        if (chat == null || chat.Key != "pbnn" || ChatManagerContext.Current != owner) return;
        try
        {
            packet = PbnnMediaPacket.Read(chat.GeneratedData, chat.Nodes?.Count ?? 0);
            episode = chat;
        }
        catch (Exception e) { Debug.LogWarning("PBNN media omitted: " + e.Message); }
    }

    private void Activate(ChatNode node)
    {
        if (!OwnsPlayback || packet == null) { Clear(); return; }
        var index = episode.Nodes.IndexOf(node);
        var segment = packet.AtNode(index);
        if (segment == shown) return;
        Clear(); // Clear first, including invalid/missing media and unassigned boundaries.
        if (segment == null || string.IsNullOrEmpty(segment.ImageBase64)) return;
        try
        {
            if (!EnsureScreen()) return;
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(segment.ImageBase64), true) ||
                texture.width > 4096 || texture.height > 4096)
                throw new ArgumentException("Image could not be decoded or exceeds 4096 pixels per side.");
            screen.mainTexture = texture;
            screen.SetFloat("_ImageAspect", (float)texture.width / texture.height);
            EnsureOverlay();
            headline.text = segment.Headline;
            source.text = segment.SourceLabel + (string.IsNullOrWhiteSpace(packet.BulletinDate) ? "" : " | Bulletin: " + packet.BulletinDate);
            overlay.SetActive(true);
            shown = segment;
        }
        catch (Exception e)
        {
            Clear();
            Debug.LogWarning("PBNN image omitted: " + e.Message);
        }
    }

    private bool EnsureScreen()
    {
        if (screen != null) return true;
        if (TvRenderer == null || ScreenShader == null || ScreenMaterialSlot < 0 ||
            ScreenMaterialSlot >= TvRenderer.sharedMaterials.Length)
        {
            Debug.LogWarning("PBNN media needs a TV renderer, valid screen material slot and screen shader.");
            return false;
        }
        originalMaterials = TvRenderer.sharedMaterials;
        var materials = (Material[])originalMaterials.Clone();
        screen = new Material(ScreenShader) { name = "PBNN runtime TV screen" };
        screen.SetFloat("_ScreenAspect", ScreenAspect > 0 ? ScreenAspect : 16f / 9f);
        screen.mainTexture = Texture2D.blackTexture;
        materials[ScreenMaterialSlot] = screen;
        TvRenderer.sharedMaterials = materials;
        return true;
    }

    private void EnsureOverlay()
    {
        if (overlay != null) return;
        overlay = new GameObject("PBNN story overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        overlay.transform.SetParent(transform, false);
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var panel = new GameObject("Lower third", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(overlay.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(.05f, .15f);
        rect.anchorMax = new Vector2(.78f, .27f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var background = panel.GetComponent<Image>();
        background.color = new Color(.025f, .045f, .08f, .94f);
        background.raycastTarget = false;
        headline = MakeText(panel.transform, "Headline", .42f, 1f, 32);
        source = MakeText(panel.transform, "Source", 0f, .42f, 18);
    }

    private static TMP_Text MakeText(Transform parent, string name, float bottom, float top, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.color = Color.white;
        text.richText = false;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, bottom);
        rect.anchorMax = new Vector2(1, top);
        rect.offsetMin = new Vector2(18, 4);
        rect.offsetMax = new Vector2(-18, -4);
        return text;
    }

    private void ContextChanged(ChatManagerContext context)
    {
        Clear();
        episode = null;
        packet = null;
    }

    private void Clear()
    {
        shown = null;
        if (overlay != null) overlay.SetActive(false);
        if (screen != null) screen.mainTexture = Texture2D.blackTexture;
        if (texture != null) { Destroy(texture); texture = null; }
    }

    private void OnDisable()
    {
        Clear();
        episode = null;
        packet = null;
        if (manager != null)
        {
            manager.OnChatLoaded -= Load;
            manager.OnChatNodeActivated -= Activate;
            manager.OnContextChanged -= ContextChanged;
            manager = null;
        }
        if (TvRenderer != null && originalMaterials != null) TvRenderer.sharedMaterials = originalMaterials;
        originalMaterials = null;
        if (screen != null) { Destroy(screen); screen = null; }
    }
}
