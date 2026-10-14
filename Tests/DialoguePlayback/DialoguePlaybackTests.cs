using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

static class Tests
{
    private static int checks;
    static void Main()
    {
        var h = new Harness();
        var node = new ChatNode { Async = true };
        Check(!h.Activate(node).MoveNext(), "async activation returns without waiting for clip");
        Check(h.Started == 1 && h.Completed == 0, "activation return is not speech completion");
        h.Tick(100.5);
        Check(h.Completed == 0, "playing clip is not complete");
        h.Voice.Stop(); // Simulate Unity stopping at the natural end.
        h.Tick(101);
        h.Tick(102);
        Check(h.Completed == 1 && h.CompletedDspTime == 101, "natural end emits completion once");

        h = new Harness();
        var sync = h.Activate(new ChatNode { Actor = new Actor { Confidence = 0.1f } });
        while (sync.MoveNext()) { }
        Check(h.Started == 1 && h.Completed == 0 && h.Voice.isPlaying,
            "confidence scheduling coroutine can finish before the audio boundary");
        h.Voice.Stop(); h.Tick(101);
        Check(h.Completed == 1, "sync dialogue completion follows audio");

        h = new Harness(); h.Activate(node).MoveNext();
        h.Voice.Stop(); h.Tick(100.25); h.Tick(102);
        Check(h.Completed == 0, "early external stop never becomes completed later");
        h = new Harness(); h.Activate(node).MoveNext();
        h.Actor.InterruptActivation(); h.Tick(102);
        Check(h.Completed == 0, "explicit interruption invalidates audio even after expected end");
        h = new Harness(); h.Activate(node).MoveNext();
        h.Voice.clip = new AudioClip(); h.Voice.Stop(); h.Tick(102);
        Check(h.Completed == 0, "replaced clip is not completed dialogue");
        h = new Harness(); h.Activate(node).MoveNext();
        h.Current = false; h.Voice.Stop(); h.Tick(102);
        Check(h.Completed == 0, "switched chat or context rejects old playback");
        h = new Harness(); h.Activate(node).MoveNext();
        ChatManager.SkipToEnd = true; h.Voice.Stop(); h.Tick(102);
        Check(h.Completed == 0, "skip rejects in-progress line");
        h = new Harness(); ChatManager.SkipToEnd = true; h.Activate(node).MoveNext();
        Check(h.Started == 0, "skip before activation cannot start capture");
        h = new Harness(); h.Current = false; h.Activate(node).MoveNext();
        Check(h.Started == 0, "stale activation cannot start capture");
        h = new Harness(); h.Activate(new ChatNode { AudioClip = null }).MoveNext();
        Check(h.Started == 0 && h.Completed == 0, "missing audio cannot produce a quote");
        h = new Harness(); h.Voice.CanPlay = false; h.Activate(node).MoveNext();
        Check(h.Started == 0, "failed AudioSource start is not captured");
        h = new Harness(); h.Voice.mute = true; h.Activate(node).MoveNext();
        Check(h.Started == 0, "muted source is not firsthand speech");
        h = new Harness(); h.Activate(node).MoveNext();
        h.Voice.mute = true; h.Tick(100.5); h.Voice.mute = false; h.Voice.Stop(); h.Tick(102);
        Check(h.Completed == 0, "muting midway invalidates entire line");
        h = new Harness(); h.Activate(node).MoveNext();
        h.Voice.pitch = 2; h.Voice.Stop(); h.Tick(102);
        Check(h.Completed == 0, "pitch changes invalidate expected end timing");
        h = new Harness(); h.Voice.pitch = 2; h.Activate(node).MoveNext();
        h.Voice.Stop(); h.Tick(100.5);
        Check(h.Completed == 1, "constant pitch scales expected duration");
        h = new Harness();
        var delayed = h.Activate(new ChatNode { Delay = 1, Async = true });
        delayed.MoveNext(); h.Actor.InterruptActivation();
        Check(!delayed.MoveNext() && h.Started == 0, "interruption during delay cannot capture generated dialogue");
        h = new Harness();
        var pending = h.Activate(new ChatNode { AudioGenerationPending = true, Async = true });
        pending.MoveNext(); h.Actor.InterruptActivation();
        Check(!pending.MoveNext() && h.Started == 0, "interrupted audio generation never reaches playback");
        h = new Harness(); h.Activate(node).MoveNext();
        h.Activate(new ChatNode { AudioClip = new AudioClip { samples = 96000 }, Async = true }).MoveNext();
        h.Tick(101);
        Check(h.Completed == 0, "new playback on actor invalidates previous observer");
        h.Voice.Stop(); h.Tick(102);
        Check(h.Completed == 1, "only replacement line can finish on reused source");
        var invalid = new DialogueAudioCompletion(100, double.NaN);
        Check(!invalid.Observe(false, true, 102) && invalid.Ended, "invalid duration cannot complete");
        Console.WriteLine($"{checks} dialogue playback boundary checks passed (Unity audio simulated).");
    }
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }

    sealed class Harness
    {
        public readonly ActorController Actor = new ActorController();
        public readonly AudioSource Voice = new AudioSource();
        public int Started;
        public int Completed;
        public double CompletedDspTime;
        public bool Current = true;
        public Harness()
        {
            ChatManager.SkipToEnd = false;
            AudioSettings.dspTime = 100;
            AudioListener.volume = 1;
            typeof(ActorController).GetField("voice", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Actor, Voice);
            typeof(ActorController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Actor, null);
            Actor.Context = new ActorContext();
        }
        public IEnumerator Activate(ChatNode node) => Actor.Activate(node, _ => Started++,
            dsp => { Completed++; CompletedDspTime = dsp; }, () => Current);
        public void Tick(double dspTime) { AudioSettings.dspTime = dspTime; Actor.TickCoroutines(); }
    }
}
