using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RemoteControl : MonoBehaviour
{
    [Serializable]
    public class ChannelEntry
    {
        public string codeName;
        public string displayName;
        public string scenePath;
        public Sprite icon;

        [NonSerialized]
        public GameObject gameObject;
    }

    public ChannelEntry this[string codeName]
    {
        get
        {
            foreach (var entry in channels)
                if (entry.codeName == codeName)
                    return entry;
            return null;
        }
    }

    [Header("Channels")]
    [SerializeField] private List<ChannelEntry> channels = new();
    [SerializeField] private string defaultScenePath = "Reset";

    private int selectedChannel = 0;
    private int currentChannel = -1;

    [Header("Overlay")]
    [SerializeField] private CanvasGroup menuGroup;
    [SerializeField] private CanvasGroup statusImage;
    [SerializeField] private CanvasGroup staticImage;

    [SerializeField] private CanvasGroup zapBar;
    [SerializeField] private TextMeshProUGUI zapTitle;
    [SerializeField] private Image zapIcon;

    [Header("Guide List")]
    [SerializeField] private Transform guideContentParent;
    [SerializeField] private GameObject guideItemPrefab;

    [Header("Input Actions")]
    public InputActionReference select;
    public InputActionReference back;
    public InputActionReference menu;
    public InputActionReference left;
    public InputActionReference right;
    public InputActionReference up;
    public InputActionReference down;
    public InputActionReference pageUp;
    public InputActionReference pageDown;

    [Header("Sound Effects")]
    public AudioSource audioSource;

    private readonly List<(InputActionReference aref, Action<InputAction.CallbackContext> cb)> _bindings = new();

    private bool _menuOpen = false;
    private bool _initalized = false;

    public bool MenuOpen
    {
        get => _menuOpen;
        set
        {
            StartCoroutine(Fade(menuGroup, value ? 1f : 0f, 0.15f));
            _menuOpen = value;
        }
    }

    public virtual void Select()
    {
        if (!_initalized) return;
        if (MenuOpen)
        {
            var channel = selectedChannel >= 0 && selectedChannel < channels.Count ? channels[selectedChannel] : null;
            if (channel != null)
            {
                MenuOpen = false;
                JumpTo(selectedChannel);
            }
        }
        else
        {
            ChatManager.IsPaused = !ChatManager.IsPaused;
            if (ChatManager.IsPaused)
                StartCoroutine(Fade(statusImage, 1f, 0.10f));
            else
                StartCoroutine(Fade(statusImage, 0f, 0.10f));
        }
    }

    public virtual void LeftArrow()
    {
        if (!_initalized) return;
    }
    public virtual void RightArrow()
    {
        if (!_initalized) return;
    }

    public virtual void UpArrow()
    {
        if (!_initalized) return;
        if (!MenuOpen) return;
        selectedChannel = Mod(selectedChannel - 1, channels.Count);
        SelectChannel(selectedChannel);
    }

    public virtual void DownArrow()
    {
        if (!_initalized) return;
        if (!MenuOpen) return;
        selectedChannel = Mod(selectedChannel + 1, channels.Count);
        SelectChannel(selectedChannel);
    }

    public virtual void PageUp()
    {
        if (!_initalized) return;
        if (channels.Count == 0) return;
        currentChannel = Mod(currentChannel + 1, channels.Count);
        SwitchScene(channels[currentChannel]);
    }

    public virtual void PageDown()
    {
        if (!_initalized) return;
        if (channels.Count == 0) return;
        currentChannel = Mod(currentChannel - 1, channels.Count);
        SwitchScene(channels[currentChannel]);
    }

    public virtual void MenuButton()
    {
        if (!_initalized) return;
        MenuOpen = !MenuOpen;
    }

    public virtual void BackButton()
    {
        if (!_initalized) return;
        if (MenuOpen)
        {
            MenuOpen = false;
            return;
        }
        else
        {
            ChatManager.RepeatLastNode = true;
        }
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        Bind(select, _ => Select());
        Bind(back, _ => BackButton());
        Bind(menu, _ => MenuButton());
        Bind(left, _ => LeftArrow());
        Bind(right, _ => RightArrow());
        Bind(up, _ => UpArrow());
        Bind(down, _ => DownArrow());
        Bind(pageUp, _ => PageUp());
        Bind(pageDown, _ => PageDown());

        Cursor.visible = false;
    }

    private void OnDestroy()
    {
        UnbindAll();
    }

    private void Start()
    {
        StartCoroutine(LoadAllChannels());
        PopulateGuide();
        SelectChannel(selectedChannel);
    }

    private void PopulateGuide()
    {
        if (!guideContentParent || !guideItemPrefab || channels.Count == 0) return;

        for (int i = guideContentParent.childCount - 1; i >= 0; i--)
            Destroy(guideContentParent.GetChild(i).gameObject);

        for (int i = 0; i < channels.Count; i++)
        {
            var entry = channels[i];
            var go = Instantiate(guideItemPrefab, guideContentParent);
            var title = go.GetComponentInChildren<TextMeshProUGUI>(true);
            var img = go.GetComponentsInChildren<Image>(true)[1];

            if (title)
                title.text = string.IsNullOrWhiteSpace(entry.displayName) ? entry.scenePath : entry.displayName;
            if (img)
                img.sprite = entry.icon;
            entry.gameObject = go;
        }
    }

    private void Bind(InputActionReference a, Action<InputAction.CallbackContext> cb)
    {
        if (!a) return;
        a.action.performed += cb;
        a.action.Enable();
        _bindings.Add((a, cb));
    }

    private void UnbindAll()
    {
        foreach (var (aref, cb) in _bindings)
        {
            if (!aref) continue;
            aref.action.performed -= cb;
            aref.action.Disable();
        }
        _bindings.Clear();
    }

    private void SwitchScene(ChannelEntry entry)
    {
        if (entry == null || entry.codeName == ChatManagerContext.Current.Key)
            return;

        if (zapTitle)
            zapTitle.text = string.IsNullOrWhiteSpace(entry.displayName) ? entry.scenePath : entry.displayName;
        if (zapIcon)
            zapIcon.sprite = entry.icon;

        StartCoroutine(LoadIntroSequence());

        if (staticImage)
            StartCoroutine(Fade(staticImage, 1f, 0.05f));
        if (zapBar)
            StartCoroutine(Fade(zapBar, 1f, 0.10f));
        if (statusImage)
            StartCoroutine(Fade(statusImage, 1f, 0.25f));

        var loadOp = SceneManager.LoadSceneAsync(entry.scenePath, LoadSceneMode.Single);
        loadOp.completed += _ =>
        {
            if (zapBar)
                StartCoroutine(Fade(zapBar, 0f, 10f));
            StartCoroutine(ExitIntroSequence());
        };
    }

    private IEnumerator Fade(CanvasGroup canvas, float target, float dur)
    {
        canvas.blocksRaycasts = true;
        canvas.interactable = true;

        float start = canvas.alpha, t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            canvas.alpha = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        canvas.alpha = target;

        bool visible = canvas.alpha > 0.001f;
        canvas.blocksRaycasts = visible;
        canvas.interactable = visible;
    }

    private void JumpTo(int index)
    {
        if (index < 0 || index >= channels.Count) return;
        currentChannel = index - 1; // reuse PageUp increment
        PageUp();
    }

    private void SelectChannel(int index)
    {
        var selected = channels[index];
        foreach (var entry in channels)
        {
            if (entry.gameObject)
            {
                var txt = entry.gameObject.GetComponentInChildren<TextMeshProUGUI>(true);
                if (txt)
                    txt.color = entry == selected ? Color.black : Color.white;
                var img = entry.gameObject.GetComponentInChildren<Image>(true);
                if (img)
                    img.color = entry == selected ? Color.white : Color.black;
            }
        }
    }

    private IEnumerator LoadChannel(ChannelEntry entry)
    {
        var scenePath = entry == null ? defaultScenePath : entry.scenePath;
        var op = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
        while (!op.isDone)
            yield return new WaitForEndOfFrame();
    }

    private IEnumerator LoadIntroSequence()
    {
        audioSource?.Play();

        if (staticImage)
            yield return Fade(staticImage, 1f, 2f);
        if (statusImage)
            yield return Fade(statusImage, 1f, 0.25f);
        yield return WaitUntilIntroSequenceEnds();
    }

    private IEnumerator ExitIntroSequence()
    {
        yield return WaitUntilIntroSequenceEnds();

        if (staticImage)
            yield return Fade(staticImage, 0f, 0.1f);
        if (statusImage)
            yield return Fade(statusImage, 0f, 0.25f);
    }

    private IEnumerator WaitUntilIntroSequenceEnds()
    {

        yield return new WaitUntil(() => audioSource?.isPlaying == false);
    }

    private IEnumerator LoadAllChannels()
    {
        StartCoroutine(LoadIntroSequence());

        foreach (var entry in channels)
            yield return LoadChannel(entry);
        yield return LoadChannel(null);
        yield return ExitIntroSequence();

        ChatManager.Instance.ResetContext();

        _initalized = true;
    }

    private static int Mod(int a, int b) => (a % b + b) % b;
}
