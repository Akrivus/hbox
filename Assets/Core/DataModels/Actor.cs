using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    public WordReplacement[] WordReplacementDictionary;

    public string ApplyWordReplacements(string text)
    {
        if (WordReplacementDictionary == null || WordReplacementDictionary.Length == 0)
            return text;
        foreach (var replacement in WordReplacementDictionary)
            text = replacement.Apply(text);
        return text;
    }

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

    [Serializable]
    public class WordReplacement
    {
        public string Word;
        public string Replacement;

        public string Apply(string text)
        {
            if (Word.StartsWith("/") && Word.EndsWith("/"))
            {
                var pattern = Word.Substring(1, Word.Length - 2);
                return Regex.Replace(text, pattern, Replacement);
            }
            return text.Replace(Word, Replacement);
        }
    }
}