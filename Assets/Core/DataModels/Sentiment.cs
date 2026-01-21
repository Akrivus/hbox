using System;
using System.Collections.Generic;
using UnityEngine;

public class Sentiment : ScriptableObject
{
    [Header("Sentiment")]
    public string Name;

    [Range(-1, 1)]
    public float Score;

    [Header("Target")]
    public GrabTarget GrabTarget;
    public GazeTarget GazeTarget;

    [Header("Appearance")]
    public Color Color;
    public Texture2D Eyes;
    public Texture2D Lips;

    [Header("Audio")]
    public AudioClip[] Sounds;
    public int MinReactions = 3;

    [Range(0, 1)]
    public float ReactionChance = 0.5f;

    [Range(0, 1)]
    public float ReactionDecay = 0.5f;

    public AudioClip Sound => (Sounds != null && Sounds.Length > 0) ? Sounds[UnityEngine.Random.Range(0, Sounds.Length)] : null;

    public class SearchableList
    {
        public Sentiment this[string name] => List.Find(sentiment => sentiment.Name == name);
        public void Add(Sentiment sentiment) => List.Add(sentiment);

        public List<Sentiment> List;

        public SearchableList()
        {
            List = new List<Sentiment>();
        }

        public SearchableList(List<Sentiment> sentiments) : this()
        {
            foreach (var sentiment in sentiments)
                List.Add(sentiment);
        }
    }
}

public enum GrabTarget
{
    None,
    Hand,
    Wrist,
    Shoulder,
    Neck,
    Waist,
    Cheek,
    Face,
    Arm
}

public enum GazeTarget
{
    None,
    Eyes,
    Lips,
    Neck,
    Hands,
    Chest,
    Ground,
    Away,
    Forehead,
    Mouth,
    Shoulders
}