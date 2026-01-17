using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class MonologueEngine : MonoBehaviour, ISubGenerator
{
    [SerializeField]
    private Actor[] narrators;

    private TextToSpeechGenerator tts;

    private void Start()
    {
        tts = GetComponent<TextToSpeechGenerator>();
    }

    public async Task<Chat> Generate(PromptResolver prompt, Chat chat)
    {
        var narratorActor = chat.ManagerContext.ActorsSearch["Narrator"];
        var actors = chat.Actors.ToList();
        actors.Add(new ActorContext(narratorActor));
        chat.Actors = actors.ToArray();

        var log = chat.Log.Split("\n").Select((l, i) => $"{i + 1}: {l}").ToArray();
        foreach (var narrator in narrators)
        {
            var controller = chat.Actors.Any(actor => actor.Reference == narrator) ? chat.Actors.Get(narrator) : null;
            if (controller == null) continue;

            var resolver = new PromptResolver(chat.ManagerContext, "Actors", "Memories", narrator.Name);
            var bucket = await MemoryBucket.Get(chat.ManagerContext, narrator.Name);

            await controller.SetPrompt(chat.Actors);

            var nodes = chat.Nodes.ToList();
            var monologue = await LLM.CompleteAsync(
                await prompt.Resolve(log, controller.Prompt, controller.Context, chat.Idea.Prompt), chat, false);

            var delivery = monologue.Find("Delivery");
            var lines = monologue.Find("Lines") ?? monologue;
            if (lines == null)
                continue;

            var l = lines.Split("\n").Select(l => l.Trim()).Reverse().ToList();

            foreach (var _ in l)
            {
                var parts = _.Split(':');
                if (parts.Length <= 1)
                    continue;
                if (int.TryParse(parts[0], out var num))
                {
                    var text = string.Join(":", parts.Skip(1)).Trim();
                    var node = new ChatNode(narrator, text);
                    node.Thoughts = delivery;
                    await tts.GenerateTextToSpeech(node);
                    node.Actor = narratorActor;
                    nodes.Insert(num - 1, node);
                }
            }
            chat.Nodes = nodes.ToList();
        }

        return chat;
    }
}
