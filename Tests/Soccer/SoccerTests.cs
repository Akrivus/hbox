using System;
using System.Linq;
using System.Threading.Tasks;

static class Tests
{
    static async Task Main()
    {
        var mixed = SoccerRosterService.Parse("```json\n{\"home\":[\"George Washington\",\"Joe Biden\",\"Donald Trump\"],\"away\":[\"Napoleon Bonaparte\"]}\n```", 3, 1, new[] { "America" });
        Check(mixed[0][0] == "George Washington" && mixed[0][2] == "Donald Trump", "mixed eras");
        Reject("{\"home\":[\"America\"],\"away\":[\"Napoleon\"]}", "cast name");
        Reject("{\"home\":[\"Joe Biden\"],\"away\":[\"joe biden\"]}", "duplicate across sides");
        Reject("{\"home\":[],\"away\":[\"Napoleon\"]}", "short roster");
        Reject("{\"home\":[42],\"away\":[\"Napoleon\"]}", "non-name");
        Reject("{\"home\":[\"Joe: hello\"],\"away\":[\"Napoleon\"]}", "dialogue injection");
        var fallback = SoccerRosterService.Fallback("America", new[] { "Joe", "Joe", "", "Donald" }, 11);
        Check(fallback.Length == 11 && fallback.Distinct().Count() == 11, "complete fallback without duplicates");
        Check(SoccerRosterService.Fallback("France", null, 11).Length == 11, "missing fallback");
        LLM.Output = "invalid JSON";
        var failed = false;
        try { await new SoccerRosterService().Generate(new ChatGenerator(), "America", "France", 11, 11, "story", Array.Empty<string>()); }
        catch (Newtonsoft.Json.JsonException) { failed = true; }
        Check(failed, "malformed generation fails the phase");
        LLM.Pending = new TaskCompletionSource<string>();
        var waiting = new SoccerRosterService().Generate(new ChatGenerator(), "America", "France", 1, 1, "story", Array.Empty<string>());
        Check(!waiting.IsCompleted, "generation awaits the model");
        LLM.Pending.SetResult("{\"home\":[\"George Washington\"],\"away\":[\"Napoleon\"]}");
        Check((await waiting)[0][0] == "George Washington", "delayed result is retained");
        Console.WriteLine("11 soccer roster checks passed.");
    }
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    static void Reject(string json, string name)
    {
        try { SoccerRosterService.Parse(json, 1, 1, new[] { "America" }); }
        catch (FormatException) { return; }
        throw new Exception("Accepted " + name);
    }
}

// Runtime boundaries are stubbed; parsing and fallback use the production implementation.
public class ChatGenerator { public object ManagerContext => null; }
public class PromptResolver
{
    public PromptResolver(object context, params string[] parts) { }
    public Task Resolve(params string[] values) => Task.CompletedTask;
}
public static class LLM
{
    public static string Output;
    public static TaskCompletionSource<string> Pending;
    public static Task<string> CompleteAsync(PromptResolver prompt, object chat) => Pending?.Task ?? Task.FromResult(Output);
}
namespace UnityEngine { public static class Debug { public static void LogWarning(string text) { } } }
