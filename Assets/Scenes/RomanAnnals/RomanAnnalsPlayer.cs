using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RomanAnnalsPlayer : MonoBehaviour
{
    [SerializeField] private ChatManagerContext context;
    [SerializeField] private VideoCallUIController tilePrefab;
    [SerializeField] private TextAsset fixture;
    [SerializeField] private Texture2D[] portraits;
    [SerializeField] private AudioClip[] voices;

    [Serializable]
    private sealed class Episode
    {
        public string id, period, year, room, title, result;
        public Participant[] cast;
        public Line[] lines;
    }

    [Serializable]
    private sealed class Participant
    {
        public string id, caption;
    }

    [Serializable]
    private sealed class Line
    {
        public string speaker, text;
    }

    private readonly Color background = new Color32(30, 31, 35, 255);
    private readonly Color muted = new Color32(181, 186, 198, 255);
    private AudioSource voice;
    private bool voicePaused;

    private IEnumerator Start()
    {
        // HBOx preloads every enabled channel. Only perform once the switch is complete.
        yield return new WaitUntil(() => ChatManager.Instance != null &&
            ChatManager.Instance.CurrentContext == context && ChatManager.Instance.ReadyForAction);

        var episode = JsonUtility.FromJson<Episode>(fixture.text);
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0;

        var canvas = new GameObject("Annals Canvas", typeof(RectTransform), typeof(Canvas));
        canvas.transform.SetParent(transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var backdrop = Box(canvas.transform, "Background");
        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
        var fill = backdrop.gameObject.AddComponent<Image>();
        fill.color = background;
        fill.raycastTarget = false;

        var stage = Box(backdrop, "Senate Call");
        var header = Label(stage, "Year", $"{episode.period}  /  {episode.year}  /  {episode.room}", 20, muted);
        var title = Label(stage, "Agenda", episode.title, 32, Color.white);
        var speaker = Label(stage, "Speaker", "", 22, new Color32(103, 213, 146, 255));
        var captions = Label(stage, "Dialogue", "", 27, Color.white);
        var credit = Label(stage, "Source", "Dramatization · Sallust, The War with Catiline 50–55", 17, muted);

        var tiles = new VideoCallUIController[episode.cast.Length];
        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i] = Instantiate(tilePrefab, stage);
            tiles[i].SetPortrait(episode.cast[i].caption, portraits[i]);
        }

        var elapsed = 0f;
        var beat = -1;
        var viewport = Vector2Int.zero;
        while (true)
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != viewport)
            {
                viewport = size;
                Arrange(stage, header, title, speaker, captions, credit, tiles, size);
            }
            if (ChatManager.IsPaused)
            {
                if (!voicePaused && voice.isPlaying)
                {
                    voice.Pause();
                    voicePaused = true;
                }
                yield return null;
                continue;
            }

            if (voicePaused)
            {
                voice.UnPause();
                voicePaused = false;
            }

            var nextBeat = false;
            if (beat < 0)
                nextBeat = elapsed >= 1.25f;
            else if (beat < episode.lines.Length)
                nextBeat = elapsed >= voices[beat].length + 0.3f;

            if (nextBeat)
            {
                beat++;
                elapsed = 0f;
                if (beat == episode.lines.Length)
                {
                    foreach (var tile in tiles)
                        tile.SetBorderOpacity(0f);
                    speaker.text = "Decision";
                    captions.text = episode.result;
                }
                else
                {
                    var line = episode.lines[beat];
                    var active = Array.FindIndex(episode.cast, person => person.id == line.speaker);
                    for (var i = 0; i < tiles.Length; i++)
                        tiles[i].SetBorderOpacity(i == active ? 1f : 0f);
                    speaker.text = episode.cast[active].caption;
                    captions.text = line.text;
                    voice.clip = voices[beat];
                    voice.Play();
                }
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static void Arrange(RectTransform stage, TMP_Text header, TMP_Text title, TMP_Text speaker,
        TMP_Text captions, TMP_Text credit, VideoCallUIController[] tiles, Vector2Int viewport)
    {
        var vertical = viewport.x < viewport.y;
        var width = vertical ? 480 : 1160;
        var height = vertical ? 840 : 540;
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
        stage.anchoredPosition = Vector2.zero;
        stage.sizeDelta = new Vector2(width, height);
        var scale = Mathf.Min(viewport.x / (float)width, viewport.y / (float)height);
        stage.localScale = Vector3.one * scale;

        Position(header.rectTransform, 20, 22, width - 40, 28);
        Position(title.rectTransform, 20, 52, width - 40, vertical ? 78 : 54);
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = (RectTransform)tiles[i].transform;
            tile.localScale = Vector3.one * (vertical ? 0.72f : 1f);
            // Preserve the prefab's centered pivot and native tile dimensions.
            tile.anchorMin = tile.anchorMax = new Vector2(0, 1);
            tile.anchoredPosition = vertical
                ? new Vector2(width / 2f, -208 - 160 * i)
                : new Vector2(200 + 380 * i, -234);
        }

        Position(speaker.rectTransform, 20, vertical ? 616 : 366, width - 40, 30);
        Position(captions.rectTransform, 20, vertical ? 654 : 404, width - 40, vertical ? 141 : 87);
        Position(credit.rectTransform, 20, height - 35, width - 40, 25);
    }

    private static void Position(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static RectTransform Box(Transform parent, string label)
    {
        var rect = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static TMP_Text Label(Transform parent, string label, string value, float size, Color color)
    {
        var text = Box(parent, label).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
