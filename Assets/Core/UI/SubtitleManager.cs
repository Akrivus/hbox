using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitleManager : MonoBehaviour
{
    public static SubtitleManager Instance { get; private set; }

    [SerializeField]
    private TextMeshProUGUI splashScreen;

    [SerializeField]
    private TextMeshProUGUI title;

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
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(SplashScreenConfigs), "splash", (_config) => Configure((SplashScreenConfigs)_config));
        Instance = this;
    }

    public void OnNodeActivated(ChatNode node)
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

    public IEnumerator StartSplashScreen(Chat chat)
    {
        if (splashScreen == null) yield break;
        yield return FadeOut(title);
        title.text = string.Empty;

        if (ChatManager.Instance.ActorsInScene.Count == 0)
        {
            if (splashes != null && splashes.Length > 0)
            {
                splashScreen.text = splashes[Random.Range(0, splashes.Length)];
                yield return FadeIn(splashScreen);

                yield return new WaitForSeconds(splashDuration);
                yield return FadeOut(splashScreen);
            }

            splashScreen.text = ChatManagerContext.Current.Name;
            yield return FadeIn(splashScreen);

            yield return new WaitForSeconds(titleDuration);
            yield return FadeOut(splashScreen);
        }

        title.text = chat.Title;
        yield return FadeIn(title);

        if (fadeOut)
        {
            yield return new WaitForSeconds(titleDuration);
            yield return FadeOut(title);
        }
    }

    private IEnumerator FadeIn(TextMeshProUGUI text)
    {
        var c = text.color = new Color(text.color.r, text.color.g, text.color.b, 0);
        var t = 0.0f;

        while (t < 1.0f)
        {
            text.color = new Color(c.r, c.g, c.b, t += Time.deltaTime);
            yield return new WaitForEndOfFrame();
        }
    }

    private IEnumerator FadeOut(TextMeshProUGUI text)
    {
        var c = text.color = new Color(text.color.r, text.color.g, text.color.b, 1);
        var t = 1.0f;

        while (t > 0.0f)
        {
            text.color = new Color(c.r, c.g, c.b, t -= Time.deltaTime);
            yield return new WaitForEndOfFrame();
        }
    }
}