using System.Linq;
using UnityEngine;

public class VibeSwitcher : MonoBehaviour
{
    [SerializeField]
    private AudioClip[] vibes;

    [SerializeField]
    private float foregroundVolume = 0.25f;

    [SerializeField]
    private float backgroundVolume = 0.05f;

    private ChatManagerContext boundContext;

    private void Start()
    {
        boundContext = ChatManagerContext.Current;
        if (boundContext == null)
            return;

        boundContext.AfterIntermission += OnAfterIntermission;
        boundContext.BeforeIntermission += OnBeforeIntermission;
    }

    private void OnDestroy()
    {
        if (boundContext == null)
            return;

        boundContext.AfterIntermission -= OnAfterIntermission;
        boundContext.BeforeIntermission -= OnBeforeIntermission;
        boundContext = null;
    }

    private void OnBeforeIntermission()
    {
        var audioSource = boundContext?.AudioSource;
        if (audioSource == null)
            return;

        audioSource.volume = foregroundVolume;
    }

    private void OnAfterIntermission(Chat chat)
    {
        var audioSource = boundContext?.AudioSource;
        if (audioSource == null)
            return;

        audioSource.volume = backgroundVolume;
        audioSource.Stop();

        if (chat.Vibe != null)
        {
            var vibe = vibes.FirstOrDefault(vibe => vibe.name == chat.Vibe);
            if (vibe != null)
            {
                audioSource.clip = vibe;
                audioSource.Play();
            }
        }
    }
}
