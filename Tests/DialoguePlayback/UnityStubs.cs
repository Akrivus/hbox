// Only the Unity/audio surface is simulated. Tests run production ActorController.Activate
// and its independently scheduled ObserveDialogueAudio coroutine, not a copied playback path.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public sealed class Actor { public float Confidence = 1; public Sentiment DefaultSentiment = new Sentiment(); }
public sealed class ActorContext { public Actor Reference = new Actor(); public Sentiment Sentiment = new Sentiment(); }
public sealed class Sentiment { public float Score; }
public sealed class Chat { }
public static class ChatManager { public static bool SkipToEnd; }
public sealed class ChatNode
{
    public float Delay;
    public bool AudioGenerationPending;
    public Actor Actor = new Actor();
    public AudioClip AudioClip = new AudioClip();
    public bool Async;
    public string Text = "Spoken reaction";
}
public interface ISubActor { void UpdateActor(ActorContext context); }
public interface ISubSentiment { void UpdateSentiment(Sentiment sentiment); }
public interface ISubNode { void Activate(ChatNode node); }
public interface ISubChats { void Initialize(Chat chat); }
public interface ISubExits { void Deactivate(); }
public static class AudioExtensions { public static float GetAmplitude(this AudioSource source) => 0; }
namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public class MonoBehaviour
    {
        public readonly List<IEnumerator> Coroutines = new List<IEnumerator>();
        public bool isActiveAndEnabled = true;
        public Transform transform = new Transform();
        public object gameObject = new object();
        protected T[] GetComponents<T>() => Array.Empty<T>();
        protected void StartCoroutine(IEnumerator routine)
        {
            // Unity advances a coroutine to its first yield immediately.
            if (routine.MoveNext()) Coroutines.Add(routine);
        }
        protected static void Destroy(object value) { }
        public void TickCoroutines()
        {
            foreach (var routine in Coroutines.ToArray())
                if (!routine.MoveNext()) Coroutines.Remove(routine);
        }
    }
    public sealed class AudioSource
    {
        public bool isPlaying;
        public bool loop;
        public bool mute;
        public float volume = 1;
        public float pitch = 1;
        public AudioClip clip;
        public Transform transform = new Transform();
        public bool CanPlay = true;
        public void Play() { isPlaying = CanPlay; }
        public void Stop() { isPlaying = false; }
    }
    public sealed class AudioClip { public int samples = 48000; public int frequency = 48000; public float length => (float)samples / frequency; }
    public static class AudioSettings { public static double dspTime; }
    public static class AudioListener { public static float volume = 1; }
    public static class Time { public static float deltaTime = 0.1f; }
    public static class Mathf
    {
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Abs(float value) => Math.Abs(value);
    }
    public struct Vector3
    {
        public float magnitude => 0;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3();
    }
    public sealed class Transform { public Vector3 position; }
    public struct Color { }
    public sealed class Camera { }
    public static class Debug { public static void LogWarning(string message) { } }
}
