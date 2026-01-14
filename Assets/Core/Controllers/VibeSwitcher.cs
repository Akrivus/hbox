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

    private void Start()
    {
        ChatManagerContext.Current.AfterIntermission += OnAfterIntermission;
        ChatManagerContext.Current.BeforeIntermission += OnBeforeIntermission;
    }

    private void OnDestroy()
    {
        ChatManagerContext.Current.AfterIntermission -= OnAfterIntermission;
        ChatManagerContext.Current.BeforeIntermission -= OnBeforeIntermission;
    }

    private void OnBeforeIntermission()
    {
        ChatManagerContext.Current.AudioSource.volume = foregroundVolume;
    }

    private void OnAfterIntermission(Chat chat)
    {
        ChatManagerContext.Current.AudioSource.volume = backgroundVolume;
        ChatManagerContext.Current.AudioSource.Stop();

        if (chat.Vibe != null)
        {
            var vibe = vibes.FirstOrDefault(vibe => vibe.name == chat.Vibe);
            if (vibe != null)
            {
                ChatManagerContext.Current.AudioSource.clip = vibe;
                ChatManagerContext.Current.AudioSource.Play();
            }
        }
    }
}