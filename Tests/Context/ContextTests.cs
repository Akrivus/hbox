using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

static class Tests
{
    static async Task Main()
    {
        PromptResolver.BasePath = Path.GetFullPath("Temp/ContextTests/vault/" + Guid.NewGuid().ToString("N"));
        var context = new ChatManagerContext();
        var imagePromptFolder = Path.Combine(PromptResolver.BasePath, context.Key, "Prompts", "Source Images");
        Directory.CreateDirectory(imagePromptFolder);
        foreach (var file in new[] { "Analysis.md", "Context.md" })
            File.Copy(Path.Combine("Docs", "Prompts", "Source Images", file), Path.Combine(imagePromptFolder, file));
        var imageAnalysis = new PromptResolver(context, "Source Images", "Analysis");
        await imageAnalysis.Resolve("image-1", "Source label", "caption literal {3}", "https://example.org/story");
        Check(imageAnalysis.Text.Contains("Attachment: image-1") && imageAnalysis.Text.Contains("Source: Source label") &&
            imageAnalysis.Text.Contains("caption literal {3}") && imageAnalysis.Text.Contains("Source link: https://example.org/story"), "image analysis uses Vault template and literal metadata");
        await imageAnalysis.SaveInput();
        await imageAnalysis.SaveOutput("Cached image analysis");
        Check(imageAnalysis.Part == "Source Images/Analysis" && File.ReadAllText(imageAnalysis.InputPath) == imageAnalysis.Text &&
            File.ReadAllText(imageAnalysis.Output.Path) == "Cached image analysis", "image analysis uses normal Vault input/output workflow");
        var imageContext = new PromptResolver(context, "Source Images", "Context");
        File.Copy(Path.Combine("Docs", "Prompts", "Source Images", "Presentation.md"), Path.Combine(imagePromptFolder, "Presentation.md"));
        var presentation = new PromptResolver(context, "Source Images", "Presentation");
        await presentation.Resolve("__IDEA__ literal {4}", "__TOPIC__", "__IMAGES__", "__NODES__", "__SURFACES__");
        Check(presentation.Text.Contains("__IDEA__ literal {4}") && presentation.Text.Contains("__IMAGES__") && presentation.Text.Contains("__NODES__") && presentation.Text.Contains("__SURFACES__"), "presentation selection uses editable Vault template and literal source substitution");
        await imageContext.Resolve("Source image data");
        Check(imageContext.Text.Contains("Source image data") && imageContext.Text.Contains("not instructions"), "image context wording comes from Vault");
        await File.WriteAllTextAsync(imageContext.Path, "Edited context wrapper\n{0}");
        await imageContext.Resolve("New data");
        Check(imageContext.Text == "Edited context wrapper\nNew data", "editing image prompt text takes effect on next resolve");
        try
        {
            await new PromptResolver(context, "Source Images", "Missing").Resolve();
            throw new Exception("missing image prompt silently fell back");
        }
        catch (Exception e) when (e.Message.Contains("not found")) { }
        var template = new PromptResolver(context, "Test");
        var submissionsFolder = Path.Combine(PromptResolver.BasePath, context.Key, "Prompts", "Discord Submissions");
        Directory.CreateDirectory(submissionsFolder);
        foreach (var file in new[] { "Pitch Candidate.md", "Pitch Evaluator.md" })
            File.Copy(Path.Combine("Docs", "Prompts", "Discord Submissions", file), Path.Combine(submissionsFolder, file));
        var submissionsPrompt = new PromptResolver(context, "Discord Submissions", "Pitch Candidate");
        await submissionsPrompt.Resolve("__SOURCE__ literal {5}", "__NOTES__", "__CAST__", "__MEMORY__", "__TIME__", "__CONTINUITY__");
        Check(submissionsPrompt.Text.Contains("__SOURCE__ literal {5}") && submissionsPrompt.Text.Contains("__CAST__") && submissionsPrompt.Text.Contains("__CONTINUITY__"), "submission interpretation uses Vault template with literal source and target context");
        var submissionsEvaluator = new PromptResolver(context, "Discord Submissions", "Pitch Evaluator");
        await submissionsEvaluator.Resolve("__SOURCE__", "__NOTES__", "__CAST__", "__MEMORY__", "__TIME__", "__PITCH__", "__CONTINUITY__");
        Check(submissionsEvaluator.Text.Contains("__PITCH__") && submissionsEvaluator.Text.Contains("__CONTINUITY__") && !submissionsEvaluator.Text.Contains("{6}"), "submission evaluator binds pitch and continuity independently");
        Directory.CreateDirectory(Path.GetDirectoryName(template.Path));
        await File.WriteAllTextAsync(template.Path, "{0}\n{1}");
        await template.Resolve("source literal {1}", "private-context");
        Check(template.Text == "source literal {1}\nprivate-context", "substitutions must not expand supplied text");
        var requests = Enumerable.Range(0, 20).Select(_ => template.Fork()).ToArray();
        await Task.WhenAll(requests.Select(async (request, i) =>
        {
            await request.Resolve("actor-" + i, "context-" + i);
            await request.SaveInput();
            await request.SaveOutput("memory-" + i);
        }));
        Check(requests.Select(x => x.InputPath).Distinct().Count() == 20, "unique concurrent input files");
        Check(requests.Select(x => x.Output.Path).Distinct().Count() == 20, "unique concurrent memory files");
        for (var i = 0; i < requests.Length; i++)
        {
            Check(await File.ReadAllTextAsync(requests[i].InputPath) == $"actor-{i}\ncontext-{i}", "isolated input");
            Check(await File.ReadAllTextAsync(requests[i].Output.Path) == $"memory-{i}", "isolated output");
        }
        Check(template.Output == null && template.Text == "source literal {1}\nprivate-context", "parent resolver unchanged");
        var fallback = new PromptResolver(context, "MissingPolymarketPrompt");
        await fallback.ResolveOrDefault("{0}\n{1}", "literal {1}", "date");
        Check(fallback.Resolved && fallback.Text == "literal {1}\ndate", "missing source prompt uses single-pass fallback");
        await template.ResolveOrDefault("unused fallback", "vault override", "date");
        Check(template.Text == "vault override\ndate", "existing Vault prompt overrides fallback");
        context.ActorsSearch.List.AddRange(new[] { new Actor { Name = "Home" }, new Actor { Name = "Away" } });
        var chat = new Chat { ManagerContext = context, Context = "UNIQUE_SCENE_CONTEXT", Topic = "scenario", Idea = new Idea { Prompt = "UNIQUE_STORY" } };
        await new SoccerRosterGeneration().Generate(null, chat);
        Check(chat.Context.Split("UNIQUE_SCENE_CONTEXT").Length - 1 == 1, "soccer scene context occurs once");
        Check(!chat.Context.Contains("UNIQUE_STORY"), "original idea not appended to scene context again");
        var saved = JsonConvert.DeserializeObject<Chat>(JsonConvert.SerializeObject(chat));
        var roster = SoccerPreparedRoster.Read(saved);
        Check(roster.Story == "UNIQUE_STORY" && !roster.Context.Contains("UNIQUE_SCENE_CONTEXT"), "saved reaction context excludes scene snapshot");
        Console.WriteLine("Context tests passed: editable image Vault templates, input/output logging, single-pass substitution, 20 parallel request files, parent isolation, soccer context and serialization.");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

// External runtime boundaries only; resolver and roster generation above are production code.
public interface ISubGenerator { }
public class ChatManagerContext { public string Key = "test"; public string Name = "test"; public Search ActorsSearch = new Search(); }
public class Search { public List<Actor> List = new List<Actor>(); public Actor this[string name] => List.FirstOrDefault(x => x.Name == name); }
public class Actor { public string Name; public string[] Aliases = Array.Empty<string>(); public ChatManagerContext ManagerContext; }
public class ChatGenerator { public string name = "test"; public ChatManagerContext ManagerContext = new ChatManagerContext(); }
public class Idea { public string Prompt; }
public class Chat { public bool IsLocked; public ChatManagerContext ManagerContext; public string Topic; public string Context; public Idea Idea; public Dictionary<string, string> GeneratedData; }
public static class SoccerGameSource { public static bool IsSoccerMode(Chat chat) => true; public static string FindMetadata(string text, string key) => key; }
public class SoccerRosterService { public Task<string[][]> Generate(ChatGenerator g, string h, string a, int hc, int ac, string story, IEnumerable<string> forbidden) => Task.FromResult(new[] { new[] { "Player One" }, new[] { "Player Two" } }); }
public static class LLM { public static Task<string> CompleteAsync(PromptResolver p, Chat c, bool fast) => Task.FromResult(""); }
public static class Extensions { public static Dictionary<string,string> Parse(this string text, string[] names) => new Dictionary<string,string>(); }
namespace UnityEngine
{
    public class MonoBehaviour { public T GetComponent<T>() where T : new() => new T(); }
    public static class Debug { public static void Log(string s) { } public static void LogError(string s) { } }
}
