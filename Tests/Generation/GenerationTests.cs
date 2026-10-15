using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

static class GenerationTests
{
    static string root;
    static int passed;
    static async Task Main()
    {
        root = Path.GetFullPath(Path.Combine("Temp", "GenerationTests", "data-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            await Run("default foreground saves before queueing", async () =>
            {
                var (g,c,i) = Setup(); await g.GenerateAndPlay(i);
                Check(ChatManager.Instance.Queued.Count == 1, "foreground queue");
                var disk = await Chat.Load(c.Name,i.Slug);
                Check(!disk.DeferAutomaticPlayback && disk.Title == "Test Bulletin", "foreground publication");
                Check(OperatorTelemetry.Completed == 1 && OperatorTelemetry.Failed == 0 && !g.IsGenerating, "completed state");
            });
            await Run("explicit save-only leaves another channel untouched", async () =>
            {
                var (g,c,i) = Setup(); var viewed = new ChatManagerContext { Key = "other", Name = "Other" };
                ChatManager.Instance.CurrentContext = viewed;
                Check(await g.GenerateEpisode(i,GenerationDisposition.SaveOnly) != null, "successful save");
                Check(ChatManager.Instance.Queued.Count == 0 && ReferenceEquals(ChatManager.Instance.CurrentContext,viewed), "channel isolation");
                Check((await Chat.Load(c.Name,i.Slug)).DeferAutomaticPlayback && OperatorTelemetry.Completed == 1 && !g.IsGenerating, "durable disposition");
            });
            await Run("Inspector queue checkbox selects save-only", async () =>
            {
                var (g,c,i) = Setup(); Set(g,"queueForPlayback",false); await g.GenerateAndPlay(i);
                Check(ChatManager.Instance.Queued.Count == 0 && (await Chat.Load(c.Name,i.Slug)).DeferAutomaticPlayback, "Inspector disposition");
            });
            await Run("explicit foreground overrides unchecked Inspector queue", async () =>
            {
                var (g,c,i) = Setup(); Set(g,"queueForPlayback",false);
                await g.GenerateEpisode(i,GenerationDisposition.SaveAndPlay);
                Check(ChatManager.Instance.Queued.Count == 1 && !(await Chat.Load(c.Name,i.Slug)).DeferAutomaticPlayback, "foreground override");
            });
            await Run("legacy save-disabled foreground remains playable", async () =>
            {
                var (g,c,i) = Setup(); Set(g,"save",false); await g.GenerateAndPlay(i);
                Check(ChatManager.Instance.Queued.Count == 1 && !Chat.FileExists(c.Name,i.Slug), "unsaved foreground compatibility");
            });
            await Run("save-only persists even if legacy save disabled", async () =>
            {
                var (g,c,i) = Setup(); Set(g,"save",false);
                Check(await g.GenerateEpisode(i,GenerationDisposition.SaveOnly) != null, "success");
                Check(Chat.FileExists(c.Name,i.Slug) && ChatManager.Instance.Queued.Count == 0, "save-only contract");
            });
            await Run("queued requests snapshot disposition and manual override", () =>
            {
                var (g,c,i) = Setup(); Set(g,"queueForPlayback",false); g.AddIdeaToQueue(i);
                g.AddPromptToQueue("manual fixture",GenerationDisposition.SaveAndPlay); Set(g,"queueForPlayback",true);
                var requests = ((IEnumerable)Get(g,"ideaQueue")).Cast<object>().ToArray();
                Check((GenerationDisposition)requests[0].GetType().GetField("Disposition").GetValue(requests[0]) == GenerationDisposition.SaveOnly, "snapshot");
                Check((GenerationDisposition)requests[1].GetType().GetField("Disposition").GetValue(requests[1]) == GenerationDisposition.SaveAndPlay, "manual override");
                return Task.CompletedTask;
            });
            await Run("active source sweep catalogs without queueing save-only", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c); await g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                await Invoke(s,"SweepForNewFiles");
                Check(ChatManager.Instance.Queued.Count == 0 && FolderSource.GetReplayStatus(c.Key).Any(x=>x.slug == i.Slug && x.eligibleNow), "catalog without queue");
                await Invoke(s,"SweepForNewFiles"); Check(ChatManager.Instance.Queued.Count == 0, "repeated sweep");
            });
            await Run("save-only is eligible for ordinary replay", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c); await g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                await Invoke(s,"SweepForNewFiles"); await Invoke(s,"FetchFiles",1);
                Check(ChatManager.Instance.Queued.Single().FileName == i.Slug, "ordinary selection");
            });
            await Run("source reconstruction preserves suppression and replay", async () =>
            {
                var (g,c,i) = Setup(); var first = Source(c); await g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                var restarted = Source(c); await Invoke(restarted,"SweepForNewFiles");
                Check(ChatManager.Instance.Queued.Count == 0, "restart suppression");
                await Invoke(restarted,"FetchFiles",1); Check(ChatManager.Instance.Queued.Single().FileName == i.Slug, "restart replay");
            });
            await Run("inactive source discovers then replays after channel return", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c); var other = new ChatManagerContext { Key="other",Name="Other" };
                ChatManager.Instance.CurrentContext = other; await g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                await Invoke(s,"SweepForNewFiles"); await Invoke(s,"FetchFiles",1);
                Check(ChatManager.Instance.Queued.Count == 0 && ReferenceEquals(ChatManager.Instance.CurrentContext,other), "inactive isolation");
                ChatManager.Instance.CurrentContext = c; await Invoke(s,"FetchFiles",1);
                Check(ChatManager.Instance.Queued.Single().Key == c.Key, "return selection");
            });
            await Run("foreign channel JSON cannot enter active source", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c);
                await new Chat { FileName=i.Slug,Key="other",ManagerContext=c }.Save();
                await Invoke(s,"SweepForNewFiles"); await Invoke(s,"FetchFiles",1);
                Check(ChatManager.Instance.Queued.Count == 0, "foreign key rejected");
            });
            await Run("legacy JSON retains incoming-file autoqueue", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c);
                await File.WriteAllTextAsync(Path.Combine(c.Name,i.Slug+".json"),JsonConvert.SerializeObject(new { Key=c.Key,FileName=i.Slug,Title="Legacy" }));
                await Invoke(s,"SweepForNewFiles"); Check(ChatManager.Instance.Queued.Single().FileName == i.Slug, "legacy discovery");
            });
            await Run("suppressed files do not consume incoming batch", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c); await g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                await new Chat { FileName="legacy",Key=c.Key,ManagerContext=c }.Save();
                await Invoke(s,"SweepForNewFiles"); Check(ChatManager.Instance.Queued.Single().FileName == "legacy", "batch skips suppression");
            });
            foreach (var d in new[] { GenerationDisposition.SaveAndPlay,GenerationDisposition.SaveOnly })
            {
                await Run(d+" generation failure not saved or queued", async () =>
                {
                    var (g,c,i) = Setup(); LLM.Failure = new InvalidOperationException("LLM failure");
                    Check(await g.GenerateEpisode(i,d) == null, "observed failure");
                    Check(!g.IsGenerating && OperatorTelemetry.Completed == 0 && OperatorTelemetry.Failed == 1, "failure state");
                    Check(!Chat.FileExists(c.Name,i.Slug) && ChatManager.Instance.Queued.Count == 0, "no publication");
                });
                await Run(d+" persistence failure observed before completion", async () =>
                {
                    var (g,c,i) = Setup(); var s = Source(c); Directory.CreateDirectory(Path.Combine(c.Name,i.Slug+".json"));
                    Check(await g.GenerateEpisode(i,d) == null, "publication failure");
                    Check(!g.IsGenerating && OperatorTelemetry.Completed == 0 && OperatorTelemetry.Failed == 1, "failure state");
                    Check(ChatManager.Instance.Queued.Count == 0 && !Directory.GetFiles(c.Name).Any(), "no queue or staging leak");
                    await Invoke(s,"SweepForNewFiles"); Check(!FolderSource.GetReplayStatus(c.Key).Any(x=>x.slug == i.Slug), "failed save not eligible");
                });
            }
            await Run("prompt persistence failure resets generation state", async () =>
            {
                var (g,c,i) = Setup(); PromptResolver.FailSave = true;
                Check(await g.GenerateEpisode(i,GenerationDisposition.SaveOnly) == null, "prompt failure");
                Check(LLM.Calls == 0 && OperatorTelemetry.Failed == 1 && !g.IsGenerating, "no paid call and reset");
            });
            await Run("in-flight generation not completed or replay eligible", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c); LLM.Gate = new TaskCompletionSource<bool>();
                var pending = g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                Check(g.IsGenerating && !pending.IsCompleted && OperatorTelemetry.Completed == 0, "pending state");
                await Invoke(s,"SweepForNewFiles"); Check(ChatManager.Instance.Queued.Count == 0 && !Chat.FileExists(c.Name,i.Slug), "no premature output");
                LLM.Gate.SetResult(true); Check(await pending != null && OperatorTelemetry.Completed == 1, "observed completion");
            });
            await Run("successful save replaces complete JSON and cleans staging", async () =>
            {
                var (g,c,i) = Setup(); var chat = await g.GenerateEpisode(i,GenerationDisposition.SaveOnly);
                chat.Title = "Replacement"; await chat.Save();
                Check((await Chat.Load(c.Name,i.Slug)).Title == "Replacement" && Directory.GetFiles(c.Name,"*.tmp").Length == 0, "replacement publication");
            });
            await Run("staging ignored by catalog and sweep", async () =>
            {
                var (g,c,i) = Setup(); var s = Source(c); await File.WriteAllTextAsync(Path.Combine(c.Name,"pending.json.staging.tmp"),"{incomplete");
                await Invoke(s,"SweepForNewFiles"); Check(ChatManager.Instance.Queued.Count == 0 && FolderSource.GetReplayStatus(c.Key).Count == 0, "staging invisible");
            });
            await Run("unlocked save fails explicitly", async () =>
            {
                var (g,c,i) = Setup(); try { await new Chat(i,c).Save(); throw new Exception("expected failure"); } catch (InvalidOperationException) { }
                Check(!Chat.FileExists(c.Name,i.Slug), "unlocked not saved");
            });
            await Run("unknown disposition rejected before generation", async () =>
            {
                var (g,c,i) = Setup(); try { await g.GenerateEpisode(i,(GenerationDisposition)99); throw new Exception("expected failure"); } catch (ArgumentOutOfRangeException) { }
                Check(LLM.Calls == 0 && !g.IsGenerating, "invalid request");
            });
            Console.WriteLine($"{passed} generation disposition regressions passed.");
        }
        finally { Directory.Delete(root,true); }
    }
    static (ChatGenerator,ChatManagerContext,Idea) Setup()
    {
        var id = Guid.NewGuid().ToString("N"); var c = new ChatManagerContext { Key=id,Name=Path.Combine(root,id) };
        Directory.CreateDirectory(c.Name); ChatManagerContext.Current = c; ChatManager.Instance = new ChatManager { CurrentContext=c };
        LLM.Failure=null; LLM.Gate=null; LLM.Calls=0; PromptResolver.FailSave=false; OperatorTelemetry.Completed=0; OperatorTelemetry.Failed=0;
        return (new ChatGenerator { Parent=c },c,new Idea("fixture"));
    }
    static FolderSource Source(ChatManagerContext c)
    {
        var s = new FolderSource(); Set(s,"boundContext",c);
        s.Configure(new FolderConfigs { ReplayDirectory=c.Name,ReplaysPerBatch=1,ReplayRate=80,MaxReplayAgeInMinutes=1440 });
        Set(s,"fileName",Path.Combine(c.Name,"history.txt")); return s;
    }
    static object Get(object o,string field) => o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    static void Set(object o,string field,object value) => o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    static async Task Invoke(object o,string method,params object[] args) => await (Task)o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);
    static void Check(bool condition,string label) { if (!condition) throw new Exception(label); }
    static async Task Run(string label,Func<Task> test) { await test(); passed++; Console.WriteLine("PASS "+label); }
}
