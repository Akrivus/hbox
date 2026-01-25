using UnityEngine;
using UnityEngine.UI;

public class StatusImageController : MonoBehaviour
{
    public Sprite PausedTexture;
    public Sprite ResumedTexture;
    public Sprite SplashTexture;

    private Image statusImage;

    void Awake()
    {
        statusImage = GetComponent<Image>();
    }

    void Start()
    {
        ChatManager.Instance.OnContextChanged += SetSplashTexture;
        ChatManager.Instance.OnPaused += SetPausedTexture;
        ChatManager.Instance.OnResumed += SetResumedTexture;
    }

    private void SetPausedTexture()
    {
        if (statusImage == null)
            return;
        statusImage.sprite = PausedTexture;
    }

    private void SetResumedTexture()
    {
        if (statusImage == null)
            return;
        statusImage.sprite = ResumedTexture;
    }

    private void SetSplashTexture(ChatManagerContext context)
    {
        if (statusImage != null)
            statusImage.sprite = SplashTexture;
    }
}
