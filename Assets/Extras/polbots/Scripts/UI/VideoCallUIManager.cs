using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class VideoCallUIManager : MonoBehaviour
{
    public static VideoCallUIManager Instance => _instance ?? (_instance = FindFirstObjectByType<VideoCallUIManager>());
    private static VideoCallUIManager _instance;

    public GameObject Container;
    public GameObject VideoScreenPrefab;
    public AudioSource AudioSource;

    [SerializeField]
    private UIGridLayoutGroup _gridLayoutGroup;

    [SerializeField]
    private SoundProfile _profile;

    private List<VideoCallUIController> _controllers = new List<VideoCallUIController>();

    private void Awake()
    {
        _instance = this;
    }

    private void Start()
    {

    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (_controllers.Count <= _gridLayoutGroup.MaxChildren) return;
        foreach (var controller in _controllers)
        {
            if (controller.IsTalking)
            {
                var index = _controllers.IndexOf(controller);
                if (index < _gridLayoutGroup.MaxChildren)
                    break;
                _controllers.Remove(controller);
                _controllers.Insert(_gridLayoutGroup.MaxChildren - 1, controller);
                break;
            }
        }
        for (var i = 0; i < _controllers.Count; i++)
            _controllers[i].transform.SetSiblingIndex(i);
    }

    public VideoCallUIController RegisterUI(ActorController actor, Camera camera)
    {
        var controller = Instantiate(VideoScreenPrefab, Container.transform)
            .GetComponent<VideoCallUIController>();
        controller.SetCamera(actor, camera);

        _controllers.Add(controller);

        return controller;
    }

    public void RemoveUI(VideoCallUIController controller)
    {
        _controllers.Remove(controller);
        controller.Hide();
        Destroy(controller.gameObject);
    }

    public void Play(VideoCallSound sound)
    {
        switch (sound)
        {
            case VideoCallSound.Mute:
                AudioSource.PlayOneShot(_profile.Mute);
                break;
            case VideoCallSound.Unmute:
                AudioSource.PlayOneShot(_profile.Unmute);
                break;
            case VideoCallSound.Ping:
                AudioSource.PlayOneShot(_profile.Ping);
                break;
            case VideoCallSound.Join:
                AudioSource.PlayOneShot(_profile.Join);
                break;
            case VideoCallSound.Leave:
                AudioSource.PlayOneShot(_profile.Leave);
                break;
        }
    }
}

public enum VideoCallSound
{
    Mute,
    Unmute,
    Ping,
    Join,
    Leave
}

[Serializable]
public class SoundProfile
{
    public AudioClip Mute;
    public AudioClip Unmute;
    public AudioClip Ping;
    public AudioClip Join;
    public AudioClip Leave;
}
