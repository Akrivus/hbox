using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;

public class SubtitlesUIManager : MonoBehaviour
{
    public static SubtitlesUIManager Instance { get; private set; }

    [SerializeField]
    private TextMeshProUGUI premierLabel;

    [SerializeField]
    private TextMeshProUGUI splashScreen;

    [SerializeField]
    private TextMeshProUGUI subtitle;

    [SerializeField]
    private TextMeshProUGUI subtitleShadow;

    [SerializeField]
    private bool fadeOut = true;

    private float titleDuration = 5f;
    private float splashDuration = 2f;
    private string[] splashes;

    public void Configure(SplashScreenConfigs c)
    {
        titleDuration = c.TitleDuration;
        splashDuration = c.SplashDuration;
        splashes = c.Splashes;

        ChatManager.Instance.OnIntermission += StartSplashScreen;
        ChatManager.Instance.OnChatNodeActivated += OnNodeActivated;
    }

    private void Awake()
    {
        Instance = this;
        ConfigManager.Instance.RegisterConfig(typeof(SplashScreenConfigs), "splash", (config) => Configure((SplashScreenConfigs)config));
    }

    private void OnNodeActivated(ChatNode node)
    {
        SetSubtitle(node.Actor.Title, node.Say, node.Actor.Color
            .Lighten()
            .Lighten());
    }

    public void ClearSubtitles()
    {
        subtitle.text = string.Empty;
        subtitleShadow.text = string.Empty;
    }

    public void SetSubtitle(string name, string text, Color color)
    {
        var content = $"<b><u>{name}</u></b>\n{text.Scrub()}";
        subtitle.text = content;
        subtitle.color = color;
        subtitleShadow.text = "<mark=#000000aa>" + content;
    }

    public void SetSubtitle(string name, string text)
    {
        SetSubtitle(name, text, Color.white);
    }

    private IEnumerator StartSplashScreen(Chat chat)
    {
        yield return FadeOut();
        splashScreen.text = string.Empty;

        if (splashes.Length > 0)
        {
            splashScreen.text = splashes[Random.Range(0, splashes.Length)];
            yield return FadeIn();

            yield return new WaitForSeconds(splashDuration);
            yield return FadeOut();
        }

        splashScreen.text = chat.Title;
        yield return FadeIn();

        if (chat.Idea.Source.StartsWith("r/"))
            SetSubtitle(chat.Idea.Source, chat.Idea.Text);

        if (fadeOut)
        {
            yield return new WaitForSeconds(titleDuration);
            yield return FadeOut();
        }
    }

    private IEnumerator FadeIn()
    {
        var c = splashScreen.color = new Color(splashScreen.color.r, splashScreen.color.g, splashScreen.color.b, 0);
        var t = 0.0f;

        while (t < 1.0f)
        {
            splashScreen.color = new Color(c.r, c.g, c.b, t += Time.deltaTime);
            yield return new WaitForEndOfFrame();
        }
    }

    private IEnumerator FadeOut()
    {
        var c = splashScreen.color = new Color(splashScreen.color.r, splashScreen.color.g, splashScreen.color.b, 1);
        var t = 1.0f;

        while (t > 0.0f)
        {
            splashScreen.color = new Color(c.r, c.g, c.b, t -= Time.deltaTime);
            yield return new WaitForEndOfFrame();
        }
    }
}