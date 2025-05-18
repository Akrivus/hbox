using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;

public class RemoteControl : MonoBehaviour
{
    private Dictionary<string, Action> buttonActions = new Dictionary<string, Action>();

    private string pageUpPath;
    private string pageDownPath;

    public virtual void Select()
    {
        ChatManager.IsPaused = !ChatManager.IsPaused;
    }

    public virtual void LeftArrow()
    {

    }

    public virtual void RightArrow()
    {

    }

    public virtual void UpArrow()
    {

    }

    public virtual void DownArrow()
    {

    }

    public virtual void PageUp()
    {
        SwitchScene(pageUpPath);
    }

    public virtual void PageDown()
    {
        SwitchScene(pageDownPath);
    }

    public virtual void MenuButton()
    {
        Application.Quit();
    }

    public virtual void BackButton()
    {

    }

    public void Configure(RemoteControlConfigs c)
    {
        buttonActions = new Dictionary<string, Action>
        {
            { "leftButton", Select },
            { "leftArrow", LeftArrow },
            { "rightArrow", RightArrow },
            { "upArrow", UpArrow },
            { "downArrow", DownArrow },
            { "pageUp", PageUp },
            { "pageDown", PageDown },
            { "rightButton", BackButton },
            { "contextMenu", MenuButton }
        };

        pageUpPath = c.PageUpPath;
        pageDownPath = c.PageDownPath;
        InputSystem.onAnyButtonPress.Call(OnPress);
    }

    private void Awake()
    {
        ConfigManager.Instance.RegisterConfig(typeof(RemoteControlConfigs), "remote_control", (config) => Configure((RemoteControlConfigs)config));
    }

    private void OnPress(InputControl control)
    {
        if (!Application.isFocused)
            return;
        if (buttonActions.TryGetValue(control.name, out var actionToInvoke))
            actionToInvoke.Invoke();
    }

    private void Launch(string path)
    {
        if (!File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path),
            UseShellExecute = true
        });
        Application.Quit();
    }

    private void SwitchScene(string scene)
    {
        SceneManager.LoadSceneAsync(scene);
        SceneManager.UnloadSceneAsync(SceneManager.GetActiveScene());
    }
}