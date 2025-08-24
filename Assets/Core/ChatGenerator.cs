using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class ChatGenerator : MonoBehaviour
{
    public int IdeaCount => ideaQueue.Count;

    [SerializeField]
    private bool save = true;

    private string slug => name.Replace(' ', '-').ToLower();

    private ISubGenerator[] generators => _generators ?? (_generators = GetComponentsInChildren<ISubGenerator>());
    private ISubGenerator[] _generators;

    private ConcurrentQueue<Idea> ideaQueue = new ConcurrentQueue<Idea>();

    private void Start()
    {
        ServerSource.AddRoute("POST", $"/generate/{slug}", (_) => ServerSource.ProcessBodyString(_, AddPromptToQueue));
        StartCoroutine(UpdateQueue());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private IEnumerator UpdateQueue()
    {
        while (Application.isPlaying)
        {
            var idea = default(Idea);
            yield return new WaitUntilTimer(() => ideaQueue.TryDequeue(out idea), 120);

            if (idea == null)
                continue;

            yield return GenerateAndPlay(idea).AsCoroutine();
        }
    }

    public void AddIdeaToQueue(Idea idea)
    {
        ideaQueue.Enqueue(idea);
    }

    public void AddPromptToQueue(string prompt)
    {
        AddIdeaToQueue(new Idea(prompt));
    }

    public async Task GenerateAndPlay(Idea idea)
    {
        var resolver = new PromptResolver(name, "Ideas");
        await resolver.SaveOutput(idea.Prompt);
        var chat = await GenerateAndSave(idea);
        ChatManager.Instance.AddToPlayList(chat);
    }

    private async Task<Chat> GenerateAndSave(Idea idea)
    {
        var chat = new Chat(idea, name);

        try
        {
            var prompt = new PromptResolver(this);
            chat = await Generate(prompt, chat);
            chat.Lock();
            if (save)
                chat.Save();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
        return chat;
    }

    private async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var options = "- " + string.Join("\n - ", GetCharacterNames());
        var secrets = string.Join(", ", GetCharacterNames(true));
        var locations = "- " + string.Join("\n - ", GetLocationNames());
        var idea = chat.Idea.Prompt;
        var context = await MemoryBucket.GetContext(slug);

        prompt = await prompt.Resolve(context, options, idea, secrets, locations);
        var topic = await LLM.CompleteAsync(prompt, false);

        var characters = topic.Find("Characters");
        if (characters != null)
        {
            chat.Actors = characters.Split(',')
                .Select(n => n.Trim())
                .Select(n => ChatManager.Instance.Actors[n])
                .OfType<Actor>()
                .Select(a => new ActorContext(a))
                .ToArray();
            topic = topic.Replace("Characters: " + characters, "");
        }

        var location = topic.Find("Location");
        if (location != null)
        {
            chat.Location = location;
            topic = topic.Replace("Location: " + location, "");
        }

        chat.Topic = topic;
        chat.Context = context;

        foreach (var g in generators)
        {
            var p = new PromptResolver(this, g);
            await g.Generate(p, chat);
        }

        return chat;
    }

    public void Receive(string message)
    {
        AddIdeaToQueue(new Idea(message));
    }

    private string[] GetCharacterNames(bool legacy = false)
    {
        return ChatManager.Instance.Actors.List
            .Select(a => string.Format("{0} ({1})", a.Name, a.Pronouns.Chomp()))
            .ToArray();
    }

    private string[] GetLocationNames()
    {
        return ChatManager.Instance.SpawnPoints
            .Select(k => k.name)
            .Shuffle()
            .ToArray();
    }
}
