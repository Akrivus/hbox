using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "New Country", menuName = "UN/Country")]
public class Actor : ScriptableObject
{
    [Header("Caption")]
    public string Title;
    public string Pronouns;

    [Header("Character")]
    public string Name;
    public string[] Aliases;
    public string[] Players;
    public Actor Neighbor;

    public bool IsLegacy;

    public string ColorScheme;
    public string Costume;

    public GameObject Prefab;

    public string Voice;
    public float SpeakingRate;
    public float Pitch;
    public float Volume;
    public float Confidence;
    public Color Color;

    public Color Color1;
    public Color Color2;
    public Color Color3;

    public Sentiment DefaultSentiment;

    public class SearchableList
    {
        public Actor this[string name] => List.Find(actor => actor.Aliases.Contains(name));
        public void Add(Actor actor) => List.Add(actor);

        public List<Actor> List;

        public SearchableList()
        {
            List = new List<Actor>();
        }

        public SearchableList(List<Actor> actors) : this()
        {
            foreach (var actor in actors)
                List.Add(actor);
            List.Sort((a, b) => a.IsLegacy.CompareTo(b.IsLegacy));
        }

        public Actor Random()
        {
            var index = UnityEngine.Random.Range(0, List.Count);
            return List[index];
        }
    }
}