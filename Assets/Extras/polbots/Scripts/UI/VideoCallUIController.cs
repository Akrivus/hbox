using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VideoCallUIController : MonoBehaviour
{
    public ActorController Controller => _actor;

    public bool IsVisible => gameObject.activeSelf;
    public bool IsMuted => _muteButton.enabled;
    public bool IsTalking => _actor.IsTalking;
    public bool IsActive { get; private set; }

    private ActorController _actor;
    private Camera _camera;
    private RenderTexture _renderTexture;

    [Header("UI Elements")]
    [SerializeField]
    private Image _container;

    [SerializeField]
    private RawImage _videoScreen;

    [SerializeField]
    private Image _muteButton;

    [SerializeField]
    private TextMeshProUGUI _caption;

    [Header("Sounds")]
    [SerializeField]
    private AudioClip _muteSound;

    [SerializeField]
    private AudioClip _unmuteSound;

    [SerializeField]
    private AudioClip _joinSound;

    [SerializeField]
    private AudioClip _exitSound;

    private void OnDestroy()
    {
        if (IsVisible)
            Hide();
        ReleaseRenderTexture();
    }

    private void ReleaseRenderTexture()
    {
        if (_renderTexture != null)
            _renderTexture.Release();
    }

    private void CreateRenderTexture()
    {
        ReleaseRenderTexture();
        var size = _videoScreen.rectTransform.sizeDelta;
        _renderTexture = new RenderTexture((int) size.x, (int) size.y, 24);
        _videoScreen.texture = _renderTexture;
        _camera.targetTexture = _renderTexture;
    }

    public void SetBorderOpacity(float value)
    {
        var color = _container.color;
        color.a = value + 0.00392156863f;
        color.a = Mathf.Clamp01(color.a);
        _container.color = color;

        IsActive = value > 0.1f;
    }

    public void SetVisibility(bool value)
    {
        IsActive = value;
        _container.enabled = value;
    }

    public void SetMuteButton(bool value)
    {
        IsActive = value;
        _muteButton.enabled = value;
    }

    public void SetCaption(string caption)
    {
        _caption.text = caption;
    }

    public void SetCamera(ActorController actor, Camera camera)
    {
        _actor = actor; _camera = camera;
        CreateRenderTexture();
        SetBorderOpacity(0);
        SetMuteButton(false);
        SetVisibility(true);
        SetCaption(actor.Actor.Title);
    }

    public void Show()
    {
        SetVisibility(true);
        VideoCallUIManager.Instance.Play(VideoCallSound.Join);
    }

    public void Hide()
    {
        SetVisibility(false);
        VideoCallUIManager.Instance.Play(VideoCallSound.Leave);
    }

    public void Mute()
    {
        _actor.Sound.enabled = false;
        SetMuteButton(true);
        VideoCallUIManager.Instance.Play(VideoCallSound.Mute);
    }

    public void Unmute()
    {
        _actor.Sound.enabled = true;
        SetMuteButton(false);
        VideoCallUIManager.Instance.Play(VideoCallSound.Unmute);
    }
}