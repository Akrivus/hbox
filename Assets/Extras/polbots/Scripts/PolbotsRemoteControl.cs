using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

public class PolbotsRemoteControl : RemoteControl
{
    public override void DownArrow()
    {
        RedditSource.Instance.TriggerDrop();
    }

    public override void UpArrow()
    {
        SoccerGameSource.Instance.ToggleGame();
    }

    public override void BackButton()
    {
        ChatManager.SkipToEnd = true;
    }

    private void Start()
    {
        ChatManager.Instance.RemoveActorsOnCompletion = false;
    }
}
