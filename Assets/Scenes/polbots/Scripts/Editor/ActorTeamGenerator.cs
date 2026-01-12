using System.IO;
using UnityEditor;
using UnityEngine;

public static class ActorTeamGenerator
{
    [MenuItem("Tools/polbots/Convert Actor Prompts")]
    public static async void ConvertActorPrompts()
    {
        var guids = AssetDatabase.FindAssets("t:Actor", new string[] { "Assets/Scenes/polbots/Actors" });
        var actors = new Actor[guids.Length];

        for (var i = 0; i < guids.Length; i++)
            actors[i] = AssetDatabase.LoadAssetByGUID(new GUID(guids[i]), typeof(Actor)) as Actor;

        string text, output;
        PromptResolver.EngineName = "polbots";

        foreach (var actor in actors)
        {
            Debug.Log(actor);
            if (actor == null) continue;
            var resolver = new PromptResolver(actor);
            await resolver.Resolve();

            text = resolver.Text;
            resolver = new PromptResolver("Character Converter");

            output = await LLM.CompleteAsync(await resolver.Resolve(actor.Name, text, actor.Pronouns), null);
            output = output
                .Replace("```markdown", string.Empty)
                .Replace("```", string.Empty)
                .Trim();

            File.WriteAllText($"./Vault/polbots/Prompts/Actors/{actor.Name}.md", output);

            Debug.Log($"Converted prompt for {actor.Name}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        PromptResolver.EngineName = null;
    }
}
