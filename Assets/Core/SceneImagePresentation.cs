using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// Opt-in, authored scene component. Core knows only the surface contract and shared plan.
public sealed class SceneImagePresentation : MonoBehaviour
{
    public event Action<ICollection<ISceneImageSurface>> OnCollectSurfaces;
    private ChatManager manager;
    private ChatManagerContext owner;
    private Chat episode;
    private SceneImagePlan plan;
    private readonly List<ChatGenerator> generators = new List<ChatGenerator>();
    private readonly Dictionary<string, ISceneImageSurface> surfaces = new Dictionary<string, ISceneImageSurface>();
    private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, SceneImageCue> shown = new Dictionary<string, SceneImageCue>();
    private int currentNode = -1;
    private bool started;

    public static T[] InScene<T>(ChatManagerContext context) where T : Component =>
        context.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    public ISceneImageSurface[] CollectSurfaces()
    {
        var context = owner != null ? owner : GetComponent<ChatManagerContext>();
        var result = InScene<SceneImageSurface>(context)
            .Where(surface => surface.IsConfigured)
            .Cast<ISceneImageSurface>()
            .ToList();
        OnCollectSurfaces?.Invoke(result);
        return result.Where(surface => surface != null && surface.IsConfigured && !string.IsNullOrWhiteSpace(surface.SurfaceId))
            .GroupBy(surface => surface.SurfaceId).Where(group => group.Count() == 1).Select(group => group.First()).ToArray();
    }

    public SceneImageSurfaceInfo[] DescribeSurfaces() => CollectSurfaces()
        .Select(surface => new SceneImageSurfaceInfo { Id = surface.SurfaceId, Description = surface.Description }).ToArray();

    private void OnEnable()
    {
        owner = GetComponent<ChatManagerContext>();
        if (owner == null)
            return;
        if (started)
            BindGenerators();
        Bind();
    }

    private void Start()
    {
        started = true;
        BindGenerators();
    }

    private void BindGenerators()
    {
        foreach (var generator in InScene<ChatGenerator>(owner))
        {
            if (generators.Contains(generator))
                continue;
            generator.OnChatGenerated += Plan;
            generators.Add(generator);
        }
    }

    private Task Plan(Chat chat)
    {
        return chat.ManagerContext == owner ? SceneImagePlanning.GenerateAsync(chat, DescribeSurfaces()) : Task.CompletedTask;
    }

    private void Bind()
    {
        if (manager != null || ChatManager.Instance == null)
            return;
        manager = ChatManager.Instance;
        manager.OnChatLoaded += Load;
        manager.OnChatNodeActivated += Activate;
        manager.OnContextChanged += ContextChanged;
    }
    
    private bool OwnsPlayback => manager != null && owner != null && episode != null &&
        manager.NowPlaying == episode && ChatManagerContext.Current == owner && !ChatManager.SkipToEnd;

    private void Update()
    {
        Bind();
        if (!OwnsPlayback)
        {
            Clear();
            return;
        }
        foreach (var id in shown.Keys.ToArray())
            if (!surfaces[id].CanDisplay(episode) || !SceneImagePlan.CanDisplay(SceneImagePlan.FindImage(episode.Idea?.Images, shown[id].ImageId)))
                ClearSurface(id);
        if (plan != null
            && currentNode >= 0
            && surfaces.Any(pair => pair.Value.IsConfigured && pair.Value.CanDisplay(episode) && !pair.Value.IsShowing && plan.AtNode(pair.Key, currentNode) != null)
        )
            Activate(episode.Nodes[currentNode]);
    }

    private void Load(Chat chat)
    {
        Clear();
        episode = null;
        plan = null;
        surfaces.Clear();
        currentNode = -1;

        if (chat == null || chat.Key != owner.Key || ChatManagerContext.Current != owner)
            return;
        try
        {
            plan = SceneImagePlan.Read(chat.GeneratedData, chat.Idea?.Images, chat.Nodes.Count);
            if (plan == null)
                return;
            var registered = CollectSurfaces();
            plan.Validate(chat.Idea?.Images, chat.Nodes.Count, registered.Select(surface => surface.SurfaceId));
            foreach (var surface in registered)
                surfaces[surface.SurfaceId] = surface;
            episode = chat;
        }
        catch (Exception e) {
            plan = null;
            Debug.LogWarning("Scene images omitted: " + e.Message);
        }
    }

    private void Activate(ChatNode node)
    {
        if (!OwnsPlayback || plan == null)
        {
            Clear();
            return;
        }
        currentNode = episode.Nodes.IndexOf(node);
        foreach (var pair in surfaces)
        {
            var id = pair.Key; var surface = pair.Value;
            var cue = surface.CanDisplay(episode) ? plan.AtNode(id, currentNode) : null;
            if (shown.TryGetValue(id, out var previous) && previous == cue && surface.IsShowing)
                continue;
            ClearSurface(id);
            if (cue == null)
                continue;
            var image = SceneImagePlan.FindImage(episode.Idea?.Images, cue.ImageId);
            if (!SceneImagePlan.CanDisplay(image))
                continue;
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                textures[id] = texture;
                if (!ImageConversion.LoadImage(texture, SceneImagePlan.ReadBytes(image), true) || texture.width > 8192 || texture.height > 8192 || (long)texture.width * texture.height > 16000000)
                    throw new ArgumentException("Unable to decode bounded scene image.");
                surface.Show(texture, cue, image);
                shown[id] = cue;
            }
            catch (Exception e)
            {
                ClearSurface(id);
                Debug.LogWarning("Scene image omitted: " + e.Message);
            }
        }
    }

    private void ClearSurface(string id)
    {
        if (surfaces.TryGetValue(id, out var surface))
            surface.Clear();
        if (textures.TryGetValue(id, out var texture))
        {
            if (texture != null)
                Destroy(texture);
            textures.Remove(id);
        }
        shown.Remove(id);
    }

    private void Clear()
    {
        foreach (var id in textures.Keys.Union(shown.Keys).ToArray())
            ClearSurface(id);
    }

    private void ContextChanged(ChatManagerContext context)
    {
        Clear();
        episode = null;
        plan = null;
    }

    private void OnDisable()
    {
        foreach (var generator in generators)
            if (generator != null)
                generator.OnChatGenerated -= Plan;
        generators.Clear();
        Clear();
        episode = null;
        plan = null;

        if (manager == null)
            return;
        manager.OnChatLoaded -= Load;
        manager.OnChatNodeActivated -= Activate;
        manager.OnContextChanged -= ContextChanged;
        manager = null;
    }
}
