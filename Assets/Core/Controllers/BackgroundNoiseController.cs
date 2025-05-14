using UnityEngine;

public class BackgroundNoiseController : AutoActor, ISubChats, ISubNode, ISubActor
{
    [SerializeField]
    private AudioSource source;

    [SerializeField]
    private MeshRenderer bgRenderer;

    private SoundGroup soundGroup;

    private void PlaySoundGroup()
    {
        if (soundGroup == null) return;
        if (soundGroup.Sounds.Length == 0)
            source.clip = null;
        else
            source.clip = soundGroup.Sounds[Random.Range(0, soundGroup.Sounds.Length)];
        if (source.clip != null)
            source.Play();
    }

    private void SetSoundGroup(string name)
    {
        var group = Resources.Load<SoundGroup>($"{ChatManager.Instance.name}/SoundGroups/{name}");
        if (group == null)
            soundGroup = Resources.Load<SoundGroup>($"{ChatManager.Instance.name}/SoundGroups/Silent");
        else
            soundGroup = group;
    }

    public void Initialize(Chat chat)
    {
        if (chat == null) return;
        var name = chat.Actors.Get(Actor).SoundGroup;
        if (name != null)
            SetSoundGroup(name);
        PlaySoundGroup();
    }

    public void Activate(ChatNode node)
    {
        if (source.isPlaying)
            return;
        PlaySoundGroup();
    }

    public void UpdateActor(ActorContext context)
    {
        var background = Resources.Load<Texture2D>($"{ChatManager.Instance.name}/Backgrounds/{Actor.Name}");
        if (background != null)
            bgRenderer.material.mainTexture = background;
    }
}
