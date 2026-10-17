using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Editor-only tool for any HBOX channel. No cast writes, episode generation or scheduling.
public sealed class ActorVoiceAuditionWindow : EditorWindow
{
    [Serializable]
    private sealed class Slot
    {
        public Actor Actor;
        public bool Override;
        public string Voice, Model, Language, Instructions;
    }
    [SerializeField] private Slot[] slots = { new Slot(), new Slot() };
    [SerializeField] private string phrase = ActorVoiceSample.StandardPhrase;
    private string status = "Select actors for A/B comparison. Nothing generates automatically.";
    [SerializeField] private string configPath = "hbox.json";
    private TTSConfigs settings;
    private bool busy;
    private bool closed;
    private AudioClip clip;
    // Unity exposes editor preview playback through AudioUtil rather than a public API.
    private static readonly Type AudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    private static MethodInfo PreviewMethod(string name, Type[] parameters) => AudioUtil?.GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, parameters, null);
    private ActorVoiceSampleCache cache;
    private Vector2 scroll;
    private static string CacheDirectory => Path.GetFullPath("Library/ActorVoiceAuditions");

    [MenuItem("HBOX/Actor Voice Auditions")]
    public static void Open() => GetWindow<ActorVoiceAuditionWindow>("Voice Auditions");

    private void OnEnable()
    {
        closed = false;
        cache = new ActorVoiceSampleCache(CacheDirectory);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }
    private void OnDisable()
    {
        closed = true;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Stop();
    }
    private void OnPlayModeChanged(PlayModeStateChange state) { Stop(); }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("A/B auditions use temporary copies of existing actors. Load a local TTS config before synthesis. No running scene is needed. Cached playback is free. Each Generate asks before one synthesis attempt; automatic retries are disabled.", MessageType.Info);
        using (new EditorGUI.DisabledScope(busy))
        {
            var path = EditorGUILayout.TextField("Local config file", configPath);
            if (path != configPath) { configPath = path; settings = null; }
            if (GUILayout.Button("Load TTS configuration (no request)"))
            {
                try
                {
                    settings = ConfigManager.LoadConfig<TTSConfigs>(configPath, "tts");
                    status = settings == null ? "No tts section found in that file." : "TTS configuration loaded locally. No synthesis requested.";
                }
                catch (Exception) { settings = null; status = "Could not read TTS configuration. Check the path and JSON format."; }
            }
            EditorGUILayout.LabelField("Shared test phrase (maximum 500 UTF-8 bytes)");
            phrase = EditorGUILayout.TextArea(phrase, GUILayout.MinHeight(50));
            for (var i = 0; i < slots.Length; i++) DrawSlot(slots[i], i == 0 ? "A" : "B");
        }
        if (GUILayout.Button("Stop playback")) Stop();
        EditorGUILayout.HelpBox(status, MessageType.None);
        EditorGUILayout.LabelField("Local cache", CacheDirectory);
        EditorGUILayout.EndScrollView();
    }

    private void DrawSlot(Slot slot, string label)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sample " + label, EditorStyles.boldLabel);
        var actor = (Actor)EditorGUILayout.ObjectField("Actor asset", slot.Actor, typeof(Actor), false);
        if (actor != slot.Actor)
        {
            slot.Actor = actor;
            slot.Override = false;
            if (actor != null)
            {
                slot.Voice = actor.Voice; slot.Model = actor.VoiceModel;
                slot.Language = actor.VoiceLanguage; slot.Instructions = actor.VoiceInstructions;
            }
        }
        if (actor == null) return;
        slot.Override = EditorGUILayout.Toggle("Temporary voice settings", slot.Override);
        using (new EditorGUI.DisabledScope(!slot.Override))
        {
            slot.Voice = EditorGUILayout.TextField("Voice", slot.Override ? slot.Voice : actor.Voice);
            slot.Model = EditorGUILayout.TextField("Gemini model (blank = config)", slot.Override ? slot.Model : actor.VoiceModel);
            slot.Language = EditorGUILayout.TextField("Language", slot.Override ? slot.Language : actor.VoiceLanguage);
            EditorGUILayout.LabelField("Voice instructions");
            slot.Instructions = EditorGUILayout.TextArea(slot.Override ? slot.Instructions : actor.VoiceInstructions);
        }
        try
        {
            var sample = Snapshot(slot);
            EditorGUILayout.LabelField("Resolved model", sample.Model);
            EditorGUILayout.LabelField("Sample ID", sample.Id.Substring(0, 16));
            var cached = cache.Read(sample);
            using (new EditorGUI.DisabledScope(cached == null))
                if (GUILayout.Button("Play cached " + label + " (free)")) Play(cached);
            using (new EditorGUI.DisabledScope(cached != null || settings == null))
                if (GUILayout.Button("Generate " + label + " — paid request…")) Generate(sample, actor);
        }
        catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Warning); }
    }

    private ActorVoiceSample Snapshot(Slot slot)
    {
        if (slot.Actor == null || !AssetDatabase.Contains(slot.Actor))
            throw new ArgumentException("Choose an existing Actor asset.");
        var copy = Instantiate(slot.Actor);
        try
        {
            if (slot.Override)
            {
                copy.Voice = slot.Voice; copy.VoiceModel = slot.Model;
                copy.VoiceLanguage = slot.Language; copy.VoiceInstructions = slot.Instructions;
            }
            return new ActorVoiceSample(copy, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(slot.Actor)),
                phrase, settings?.GoogleGeminiModel ?? new TTSConfigs().GoogleGeminiModel);
        }
        finally { DestroyImmediate(copy); }
    }

    private async void Generate(ActorVoiceSample sample, Actor actor)
    {
        if (busy || settings == null) return;
        var requestSettings = settings;
        if (!EditorUtility.DisplayDialog("Paid voice audition",
            "Synthesize one short sample using " + sample.Voice + " / " + sample.Model +
            "?\nLanguage: " + sample.Language + "\n\n" + sample.Phrase +
            "\n\nInstructions: " + sample.Instructions +
            "\n\nThis may incur provider charges. No automatic retries. Playback afterward is free.",
            "Synthesize once", "Cancel")) return;
        busy = true;
        status = "Synthesizing one sample…";
        Actor copy = null;
        try
        {
            copy = Instantiate(actor);
            copy.hideFlags = HideFlags.HideAndDontSave;
            sample.ApplyTo(copy);
            await cache.Generate(sample, true, async request =>
            {
                var node = new ChatNode { Actor = copy, Say = request.Phrase };
                await TextToSpeechGenerator.GenerateAudition(node, requestSettings);
                return new ActorVoiceSampleCache.Audio { Id = request.Id, Pcm = node.AudioData, Frequency = node.Frequency };
            });
            status = "Sample cached. Use Play cached to listen.";
        }
        catch (Exception) { status = "Audition failed; no usable sample cached. Check credentials, model, voice, connection and console. Retrying requires another explicit confirmation."; }
        finally
        {
            if (copy != null) DestroyImmediate(copy);
            busy = false;
            if (!closed) Repaint();
        }
    }

    private void Play(ActorVoiceSampleCache.Audio audio)
    {
        Stop();
        var play = PreviewMethod("PlayPreviewClip", new[] { typeof(AudioClip), typeof(int), typeof(bool) });
        if (play == null)
            throw new NotSupportedException("This Unity version does not expose AudioUtil.PlayPreviewClip.");
        clip = audio.Pcm.ToAudioClip(audio.Frequency);
        play.Invoke(null, new object[] { clip, 0, false });
        status = "Playing cached provider audio at neutral pitch (no scene voice modulation).";
    }
    private void Stop()
    {
        if (clip != null)
            PreviewMethod("StopAllPreviewClips", Type.EmptyTypes)?.Invoke(null, null);
        if (clip != null) DestroyImmediate(clip);
        clip = null;
    }
}
