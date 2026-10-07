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
        var template = new PromptResolver(context, "Test");
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
        context.ActorsSearch.List.AddRange(new[] { new Actor { Name = "Home" }, new Actor { Name = "Away" } });
        var chat = new Chat { ManagerContext = context, Context = "UNIQUE_SCENE_CONTEXT", Topic = "scenario", Idea = new Idea { Prompt = "UNIQUE_STORY" } };
        await new SoccerRosterGeneration().Generate(null, chat);
        Check(chat.Context.Split("UNIQUE_SCENE_CONTEXT").Length - 1 == 1, "soccer scene context occurs once");
        Check(!chat.Context.Contains("UNIQUE_STORY"), "original idea not appended to scene context again");
        var saved = JsonConvert.DeserializeObject<Chat>(JsonConvert.SerializeObject(chat));
        var roster = SoccerPreparedRoster.Read(saved);
        Check(roster.Story == "UNIQUE_STORY" && !roster.Context.Contains("UNIQUE_SCENE_CONTEXT"), "saved reaction context excludes scene snapshot");
        Console.WriteLine("Context tests passed: single-pass substitution, 20 parallel request files, parent isolation, soccer context and serialization.");
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
