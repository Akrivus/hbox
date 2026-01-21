using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class ChatGenerator : MonoBehaviour
{
    public ChatManagerContext ManagerContext => chatManagerContext;

    public bool IsActive { get; private set; }

    [SerializeField]
    private bool save = true;

    public string slug => name.Replace(' ', '-').ToLower();
    public string href => $"/generate/{slug}";

    private ISubGenerator[] generators => _generators ?? (_generators = GetComponents<ISubGenerator>());
    private ISubGenerator[] _generators;

    private ChatManagerContext chatManagerContext;

    private ConcurrentQueue<Idea> ideaQueue = new ConcurrentQueue<Idea>();

    private void Start()
    {
        chatManagerContext = ChatManagerContext.Current;
        ServerSource.AddRoute("POST", href, (_) => ServerSource.ProcessBodyString(_, AddPromptToQueue));
        ServerSource.Instance.AddGenerator(this);
        StartCoroutine(UpdateQueue());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private IEnumerator UpdateQueue()
    {
        IsActive = true;
        do
        {
            var idea = default(Idea);
            yield return new WaitUntilTimer(() => ideaQueue.TryDequeue(out idea), 120);

            if (idea == null)
                continue;

            yield return GenerateAndPlay(idea).AsCoroutine();
        } while (chatManagerContext != null && chatManagerContext.IsActive);
        IsActive = false;
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
        var resolver = new PromptResolver(chatManagerContext, name, "Ideas");
        await resolver.SaveOutput(idea.Prompt);
        var chat = await GenerateAndSave(idea);
        ChatManager.Instance.AddToPlayList(chat);
    }

    private async Task<Chat> GenerateAndSave(Idea idea)
    {
        UiEventBus.Publish(chatManagerContext, $"Generating idea: {idea.Prompt}");
        var chat = new Chat(idea, chatManagerContext);

        try
        {
            var prompt = new PromptResolver(this);
            chat = await Generate(prompt, chat);
            chat.Lock();
            if (save)
                chat.Save();
            UiEventBus.Publish(chatManagerContext, $"Generated new scene: {chat.Title}");
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
        var context = await MemoryBucket.GetContext(chat.ManagerContext, slug);

        prompt = await prompt.Resolve(context, options, idea, secrets, locations);
        var topic = await LLM.CompleteAsync(prompt, chat, false);

        var characters = topic.Find("Characters");
        if (characters != null)
        {
            chat.Actors = characters.Split(',')
                .Select(n => n.Trim())
                .Select(n => chatManagerContext.ActorsSearch[n])
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

        var tasks = new List<Task>();

        foreach (var g in generators)
        {
            UiEventBus.Publish(chatManagerContext, $"Running generator: {g.GetType().Name}");
            var p = new PromptResolver(this, g);

            if (g.IsBlocking)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                await g.Generate(p, chat);
            }
            else
            {
                tasks.Add(g.Generate(p, chat));
            }
        }

        UiEventBus.Publish(chatManagerContext, "Finalizing generators");
        await Task.WhenAll(tasks);

        return chat;
    }

    public void Receive(string message)
    {
        AddIdeaToQueue(new Idea(message));
    }

    private string[] GetCharacterNames(bool legacy = false)
    {
        return chatManagerContext.Actors
            .Select(a => string.Format("{0} ({1})", a.Name, a.Pronouns.Chomp()))
            .ToArray();
    }

    private string[] GetLocationNames()
    {
        return chatManagerContext.Locations
            .Shuffle()
            .ToArray();
    }
}
