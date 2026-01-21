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

    private void Start()
    {
        ChatManager.Instance.OnChatLoaded += ToggleFeed;
        ChatManager.Instance.OnChatQueueEmpty += ShowFeed;
        UiEventBus.OnEvent += OnUiEvent;
    }

    private void OnDestroy()
    {
        ChatManager.Instance.OnChatLoaded -= ToggleFeed;
        ChatManager.Instance.OnChatQueueEmpty -= ShowFeed;
        UiEventBus.OnEvent -= OnUiEvent;
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

        var tmp = feedItem.GetComponentInChildren<TextMeshProUGUI>();
        tmp.text = e.message;
        if (e.countdown.HasValue)
            StartCoroutine(UpdateEventCountdown(tmp, e.countdown.Value));
        var img = feedItem.GetComponentsInChildren<Image>(true)[1];
        if (img && remote)
            img.sprite = remote[e.channelCode].icon;

        StartCoroutine(FadeAndDie(feedItem, e));
    }

    private IEnumerator UpdateEventCountdown(TextMeshProUGUI tmp, DateTime countdown)
    {
        while (tmp != null)
        {
            var remaining = countdown - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                tmp.text = "00:00";
                yield break;
            }
            tmp.text = $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            tmp.transform.parent.SetAsLastSibling();
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator FadeAndDie(GameObject go, UiEvent e)
    {
        if (!go)
            yield break;
        var canvas = go.GetComponent<CanvasGroup>();
        var feedCount = feed.Count;

        yield return new WaitUntil(() => e.IsComplete);
        yield return new WaitForSeconds(e.lifetimeSeconds);

        if (e.lifetimeSeconds > 0)
            yield return Fade(canvas, 0f, e.lifetimeSeconds);

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
