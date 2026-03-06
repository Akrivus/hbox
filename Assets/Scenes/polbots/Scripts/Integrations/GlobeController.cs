using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using WPM;

public class GlobeController : MonoBehaviour
{
    public static int MinPopulation = 5_000_000;
    public static int MaxPopulation = 1_000_000_000;

    public WorldMapGlobe Globe => WorldMapGlobe.instance;

    public float Scale;
    public float PushAngle;

    public float MinScale = 0.5f;
    public float MaxScale = 3.0f;
    public float DownScaleIncrement = 0.1f;
    public Vector3 RotationOffset;

    public float GlobeRadius = 1f;
    public float ScaleFactor = 5f;

    public float MinZoomLevel = 0.25f;
    public float MaxZoomLevel = 0.75f;

    private Actor _lastActor;
    private GlobeEntry _lastEntry;
    private float _energy;
    private bool _zoomTo = true;

    public Dictionary<Actor, GlobeEntry> GlobeEntries = new Dictionary<Actor, GlobeEntry>();

    public void Disable()
    {
        ChatManagerContext.Current.RemoveActorsOnCompletion = false;
        ChatManagerContext.Current.DisableSoundEffects = false;

        if (VideoCallUIManager.Instance != null)
            VideoCallUIManager.Instance.Enabled = true;
        if (Camera.main != null)
            Camera.main.cullingMask = 122879;
        _zoomTo = false;
        Globe?.ZoomTo(85.0f);
    }

    public void Enable()
    {
        
        ChatManagerContext.Current.RemoveActorsOnCompletion = true;
        ChatManagerContext.Current.DisableSoundEffects = true;

        if (VideoCallUIManager.Instance != null)
            VideoCallUIManager.Instance.Enabled = false;
        if (Camera.main != null)
            Camera.main.cullingMask = 65535;
        _zoomTo = true;
        Globe?.ZoomTo(MaxZoomLevel);
    }

    private void Start()
    {
        ChatManagerContext.Current.OnChatQueueTaken += OnChatDequeued;
        ChatManagerContext.Current.OnChatLoaded += OnChatLoaded;
        ChatManagerContext.Current.OnActorAdded += OnActorAdded;
        ChatManagerContext.Current.OnActorRemoved += OnActorRemoved;
        ChatManagerContext.Current.OnChatNodeActivated += OnChatNodeActivated;

        if (Globe != null)
            Globe.OnFlyEnd += OnFlyEnd;
    }

    private void OnDestroy()
    {
        ChatManagerContext.Current.OnChatQueueTaken -= OnChatDequeued;
        ChatManagerContext.Current.OnChatLoaded -= OnChatLoaded;
        ChatManagerContext.Current.OnActorAdded -= OnActorAdded;
        ChatManagerContext.Current.OnActorRemoved -= OnActorRemoved;
        ChatManagerContext.Current.OnChatNodeActivated -= OnChatNodeActivated;

        if (Globe != null)
            Globe.OnFlyEnd -= OnFlyEnd;

        Disable();
    }

    private void Update()
    {
        foreach (var entry in GlobeEntries.Values)
            if (entry.Transform != null)
                UpdateBillboard(entry.Transform);
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(Vector3.zero, GlobeRadius);

        foreach (var a in GlobeEntries.Values)
        {
            var radius = a.Scale * ScaleFactor;
            float visualAngleA = Mathf.Atan2(radius, GlobeRadius) * Mathf.Rad2Deg;
            Gizmos.DrawWireSphere(a.Location, radius);
            Debug.DrawRay(Vector3.zero, a.Location.normalized, Color.white);
            Debug.DrawRay(Vector3.zero, Quaternion.AngleAxis(visualAngleA, Vector3.Cross(a.Location, Vector3.up)) * a.Location.normalized, Color.red);
        }
    }

    private void UpdateBillboard(Transform actor)
    {
        var toCamera = Camera.main.transform.position - actor.position;
        var radialUp = Camera.main.transform.up;
        actor.rotation = Quaternion.LookRotation(toCamera, radialUp)
            * Quaternion.Euler(RotationOffset);
    }

    private void OnFlyEnd(Vector3 location)
    {
        if (!_zoomTo)
            return;
        var zoomScaleFactor = Mathf.Clamp01(_lastEntry.Scale / Scale);
        var zoom = (1f - Mathf.Abs(_energy)) * zoomScaleFactor * MaxZoomLevel;
        Globe.ZoomTo(MinZoomLevel + zoom);
    }

    private IEnumerator OnChatDequeued(Chat chat)
    {
        if (chat.Topic.Contains("Mode: Globe"))
            Enable();
        else
            Disable();
        yield return ChatManager.Instance.RemoveAllActors();
    }

    private void OnChatLoaded(Chat chat)
    {
        if (!_zoomTo)
            return;
        foreach (var actor in chat.Actors)
            AddActorEntry(actor);
        CalculateScale();
        ResolveOverlaps();
    }

    private void OnActorAdded(Chat chat, ActorController cont)
    {
        var entry = GlobeEntries.GetValueOrDefault(cont.Actor);
        if (entry == null)
            return;
        cont.transform.position = Globe.transform.position;
        entry.Transform = cont.transform;
        Globe.AddMarker(cont.gameObject, entry.Location, entry.Scale);
    }

    private void OnActorRemoved(Chat chat, ActorController cont)
    {
        GlobeEntries.Remove(cont.Actor);
    }

    private void OnChatNodeActivated(ChatNode node)
    {
        if (_lastActor == node.Actor)
            return;
        var entry = GlobeEntries.GetValueOrDefault(node.Actor);
        if (entry == null)
            return;
        Globe.FlyToLocation(entry.Location);
        _lastEntry = entry;
        _lastActor = node.Actor;
        _energy = Mathf.Abs(node.Energy);
    }

    private Country GetCountry(Actor actor)
    {
        for (var i = 0; i < Globe.countries.Length; i++)
        {
            var aliases = actor.Neighbor == null ? actor.Aliases : actor.Neighbor.Aliases;
            if (aliases.Contains(Globe.countries[i].name))
                return Globe.countries[i];
        }
        return Globe.countries[0];
    }

    private void AddActorEntry(ActorContext context)
    {
        var city = Globe.cities.FirstOrDefault(c => c.name == context.SpawnPoint);
        var country = GetCountry(context.Reference);
        var query = Globe.cities.Where((cont) => Globe.countries[cont.countryIndex] == country);

        if (city == null)
            if (context.Reference.Neighbor != null)
                city = query.Sample();
            else
                city = query.FirstOrDefault();
        if (city == null && country.cityCapitalIndex > 0)
            city = Globe.cities[country.cityCapitalIndex];

        var population = 5_166_000; // population of palestine, which doesn't have a city????

        if (city != null)
        {
            population = city.population;
            if (context.Reference.Neighbor == null)
                population = query.Sum((city) => city.population);
        }

        var actor = context.Reference;
        var entry = new GlobeEntry(actor, country, city, population);
        GlobeEntries[actor] = entry;
    }

    private void CalculateScale()
    {
        foreach (var entry in GlobeEntries.Values)
        {
            float logMin = Mathf.Log10(MinPopulation);
            float logMax = Mathf.Log10(MaxPopulation);
            float logP = Mathf.Log10(entry.Population);

            var t = (logP - logMin) / (logMax - logMin);

            entry.Scale = Mathf.Lerp(MinScale, MaxScale, t)
                * Scale
                * entry.Actor.Pitch;
        }
    }

    private void ResolveOverlaps(int i = 0)
    {
        var entries = new List<GlobeEntry>();
        var overlap = true;

        while (overlap && i++ < 999)
        {
            overlap = false;
            entries = GlobeEntries.Select(kvp => kvp.Value).OrderByDescending(v => v.Scale).ToList();
            foreach (var a in entries)
                foreach (var b in entries)
                    if (TestOverlap(a, b))
                        overlap = true;
        }
    }

    private bool TestOverlap(GlobeEntry a, GlobeEntry b)
    {
        if (a == b) return true;

        float angleBetween = Vector3.Angle(a.Location, b.Location);

        float visualAngleA = Mathf.Atan2(a.Scale * ScaleFactor, GlobeRadius) * Mathf.Rad2Deg;
        float visualAngleB = Mathf.Atan2(b.Scale * ScaleFactor, GlobeRadius) * Mathf.Rad2Deg;

        float requiredSeparation = visualAngleA + visualAngleB;

        if (angleBetween < requiredSeparation)
        {
            var strength = Mathf.Clamp01(1f - (angleBetween / requiredSeparation));
            var direction = Vector3.ProjectOnPlane(b.Location - a.Location, a.Location).normalized;
            var axis = Vector3.Cross(b.Location, direction).normalized;

            a.Scale *= 1f - DownScaleIncrement * strength * a.Scale;
            a.Location = Quaternion.AngleAxis(PushAngle * strength, axis) * a.Location;
            b.Scale *= 1f - DownScaleIncrement * strength * b.Scale;
            b.Location = Quaternion.AngleAxis(PushAngle * -strength, axis) * b.Location;
        }

        return true;
    }

    public class GlobeEntry
    {
        public Transform Transform { get; set; }
        public Actor Actor { get; }
        public Country Country { get; }
        public City City { get; }
        public Vector3 Location { get; set; }
        public float Scale { get; set; }
        public int Population { get; }

        public GlobeEntry(Actor actor, Country country, City city, int population = 0)
        {
            Population = population;
            Actor = actor;
            Country = country;
            City = city;

            if (city != null)
                Location = Conversion.GetSpherePointFromLatLon(city.latlon);
            else
                Location = Conversion.GetSpherePointFromLatLon(country.latlonCenter);
        }
    }
}
