using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UiEventFeed : MonoBehaviour
{
    [SerializeField]
    private RectTransform eventFeed;

    [SerializeField]
    private GameObject eventFeedPrefab;

    [SerializeField]
    private CanvasGroup rootCanvas;

    [SerializeField]
    private RemoteControl remote;

    private Queue<GameObject> feed = new Queue<GameObject>();
    private Dictionary<string, UiEvent> pins = new Dictionary<string, UiEvent>();

    private void Start()
    {
        ChatManager.Instance.OnChatLoaded += ToggleFeed;
        ChatManager.Instance.OnChatQueueEmpty += ShowFeed;
        UiEventBus.OnEvent += OnUiEvent;
        StartCoroutine(UpdatePins());
    }

    private void OnDestroy()
    {
        ChatManager.Instance.OnChatLoaded -= ToggleFeed;
        ChatManager.Instance.OnChatQueueEmpty -= ShowFeed;
        UiEventBus.OnEvent -= OnUiEvent;
        StopAllCoroutines();
    }

    private void ShowFeed()
    {
        StartCoroutine(Fade(rootCanvas, 1f, 1f));
    }

    private void ToggleFeed(Chat chat)
    {
        if (chat.NewEpisode)
            StartCoroutine(Fade(rootCanvas, 0f, 1f));
        else
            ShowFeed();
    }

    private void OnUiEvent(UiEvent e)
    {
        PushToast(e);
    }

    private void PushToast(UiEvent e)
    {
        if (!eventFeed || !eventFeedPrefab) return;

        var feedItem = Instantiate(eventFeedPrefab, eventFeed);
        feed.Enqueue(feedItem);
        e.obj = feedItem;

        var img = feedItem.GetComponentsInChildren<Image>(true)[1];
        if (img && remote)
            img.sprite = remote[e.ChannelCode].icon;

        var tmp = feedItem.GetComponentInChildren<TextMeshProUGUI>();
        tmp.text = e.Message;
        if (e.IsPinned || UpdateEventCountdown(tmp, e.Countdown))
        {
            if (pins.TryGetValue(e.ChannelCode, out var oe))
                Destroy(oe.obj);
            pins[e.ChannelCode] = e;
        }

        StartCoroutine(FadeAndDie(feedItem, e));
    }

    private IEnumerator UpdatePins()
    {
        while (Application.isPlaying)
        {
            var toRemove = new List<string>();
            foreach (var e in pins.Values)
            {
                if (e.obj != null && (e.IsPinned || UpdateEventCountdown(e.obj.GetComponentInChildren<TextMeshProUGUI>(), e.Countdown)))
                    continue;
                toRemove.Add(e.ChannelCode);
            }
            foreach (var to in toRemove)
                pins.Remove(to);
            yield return new WaitForSeconds(1f);
        }
    }

    private bool UpdateEventCountdown(TextMeshProUGUI tmp, DateTime? countdown)
    {
        if (tmp == null || !countdown.HasValue)
            return false;
        var datetime = countdown.Value;
        var remaining = datetime - DateTime.Now;
        if (remaining.TotalSeconds <= 0)
            tmp.text = "00:00";
        else if (remaining.Hours > 0)
            tmp.text = $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        else
            tmp.text = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        tmp.transform.parent.SetAsLastSibling();
        return remaining.TotalSeconds >= 0;
    }

    private IEnumerator FadeAndDie(GameObject go, UiEvent e)
    {
        if (!go)
            yield break;
        var canvas = go.GetComponent<CanvasGroup>();
        var feedCount = feed.Count;

        yield return new WaitUntil(() => DateTime.Now > e.DateTime);

        if (e.LifetimeInSeconds > 0)
            yield return Fade(canvas, 0f, e.LifetimeInSeconds);

        if (go)
            Destroy(go);
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
}
