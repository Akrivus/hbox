using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class VibeSwitcher : MonoBehaviour
{
    [SerializeField]
    private AudioClip[] vibes;

    [SerializeField]
    private AudioSource _audioSource;

    [SerializeField]
    private float foregroundVolume = 0.25f;

    [SerializeField]
    private float backgroundVolume = 0.05f;

    private void Start()
    {
        ChatManager.Instance.AfterIntermission += OnAfterIntermission;
        ChatManager.Instance.BeforeIntermission += OnBeforeIntermission;
    }

    private void OnDestroy()
    {
        ChatManager.Instance.AfterIntermission -= OnAfterIntermission;
        ChatManager.Instance.BeforeIntermission -= OnBeforeIntermission;
    }

    private void OnBeforeIntermission()
    {
        _audioSource.volume = foregroundVolume;
    }

    private void OnAfterIntermission(Chat chat)
    {
        _audioSource.volume = backgroundVolume;
        _audioSource.Stop();

        if (chat.Vibe != null)
        {
            var vibe = vibes.FirstOrDefault(vibe => vibe.name == chat.Vibe);
            if (vibe != null)
            {
                _audioSource.clip = vibe;
                _audioSource.Play();
            }
        }
    }
}