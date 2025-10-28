using System;
using System.Collections;
using UnityEngine;

public class ActorController : MonoBehaviour
{
    public static float GlobalSpeakingRate = 1.0f;

    public event Func<IEnumerator> BeforeDestroy;
    public event Func<IEnumerator> AfterCreate;
    public event Func<ChatNode, IEnumerator> OnActivation;
    public event Action<ActorController> OnActorUpdate;
    public event Action<Sentiment> OnSentimentUpdate;

    public float TotalVolume => voice.GetAmplitude() + sound.GetAmplitude();
    public float VoiceVolume => voice.GetAmplitude();
    public bool IsTalking => voice.isPlaying && VoiceVolume > 0.0f;
    public bool IsNoLongerTalking => !voice.isPlaying || talkTime > 2.0f && averageVolume < 0.002f;

    public float Speed { get; private set; }
    public float Energy { get; private set; }

    public Transform RightHandTarget { get; set; }
    public Transform LeftHandTarget { get; set; }
    public Transform LookTarget { get; set; }
    public Transform LookObject => voice.transform;
    public AudioSource Voice => voice;
    public AudioSource Sound => sound;
    public Camera Camera { get; set; }

    public Color TextColor;

    [SerializeField]
    private AudioSource voice;

    [SerializeField]
    private AudioSource sound;

    private float talkTime = 0.0f;
    private float averageVolume = 1.0f;

    public ActorContext Context
    {
        get => _context;
        set
        {
            _context = value;
            OnUpdateActorCallbacks(value);
        }
    }

    public Actor Actor => Context.Reference;

    public Sentiment Sentiment
    {
        get => _sentiment ?? Actor.DefaultSentiment;
        set
        {
            _sentiment = value;
            OnUpdateSentimentCallbacks(value);
        }
    }
    
    private Sentiment _sentiment;
    private ActorContext _context;
    private Vector3 position;

    private ISubActor[] sub_Actor;
    private ISubSentiment[] sub_Sentiment;
    private ISubNode[] sub_Nodes;
    private ISubChats[] sub_Chats;
    private ISubExits[] sub_Exits;

    private void Awake()
    {
        sub_Actor = GetComponents<ISubActor>();
        sub_Sentiment = GetComponents<ISubSentiment>();
        sub_Nodes = GetComponents<ISubNode>();
        sub_Chats = GetComponents<ISubChats>();
        sub_Exits = GetComponents<ISubExits>();
    }

    private void Update()
    {
        Speed = (transform.position - position).magnitude * Time.deltaTime;
        position = transform.position;
        averageVolume = Mathf.Lerp(averageVolume, VoiceVolume, Time.deltaTime);
        talkTime += Time.deltaTime * (IsTalking ? 1.0f : 0.0f);
    }

    public void OnUpdateActorCallbacks(ActorContext context)
    {
        Sentiment = context.Sentiment;
        foreach (var subActor in sub_Actor)
            subActor.UpdateActor(context);
        OnActorUpdate?.Invoke(this);
    }

    public void OnUpdateSentimentCallbacks(Sentiment sentiment)
    {
        if (sentiment == null) return;
        Energy = sentiment.Score - Energy;
        foreach (var sub in sub_Sentiment)
            sub.UpdateSentiment(sentiment);
        OnSentimentUpdate?.Invoke(sentiment);
    }

    public IEnumerator Activate(ChatNode node)
    {
        yield return new WaitForSeconds(node.Delay);

        if (OnActivation != null)
            yield return OnActivation(node);
        foreach (var subNode in sub_Nodes)
            subNode.Activate(node);

        var clip = node.AudioClip;
        if (clip == null)
            yield break;
        
        voice.clip = clip;
        voice.Play();

        averageVolume = 1.0f;
        talkTime = 0.0f;

        if (!node.Async)
            yield return new WaitForSeconds(0.1f - Mathf.Abs(Sentiment.Score * Energy) * 0.1f + 0.9f * clip.length);
    }

    public IEnumerator Initialize(Chat chat)
    {
        foreach (var sub in sub_Chats)
            sub.Initialize(chat);
        if (AfterCreate != null)
            yield return AfterCreate();
    }

    public IEnumerator Deactivate()
    {
        foreach (var sub in sub_Exits)
            sub.Deactivate();
        if (BeforeDestroy != null)
            yield return BeforeDestroy();
        Destroy(gameObject);
    }
}