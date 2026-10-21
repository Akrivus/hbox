using System;
using System.IO;
using System.Linq;
using FStudio.MatchEngine;
using Newtonsoft.Json.Linq;

static class SavedMatchTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { return; } catch (FormatException) { return; }
        throw new Exception(message);
    }
    public static void Run()
    {
        SetupSnapshotTests.Run();
        SoccerSimulationRandom.Reset(-17);
        var first = Enumerable.Range(0, 1000).Select(_ => SoccerSimulationRandom.Range(-200, 200)).ToArray();
        SoccerSimulationRandom.Reset(-17);
        var cosmetic = new Random(99);
        for (var i = 0; i < first.Length; i++)
        {
            cosmetic.Next();
            Check(first[i] == SoccerSimulationRandom.Range(-200, 200), "seeded gameplay isolated from presentation RNG");
            Check(first[i] >= -200 && first[i] < 200, "integer range bounds");
        }
        SoccerSimulationRandom.Reset(0);
        Check(Enumerable.Range(0, 20).Select(_ => SoccerSimulationRandom.Range(0, 100)).Distinct().Count() > 1, "zero seed must not lock xorshift");
        SoccerSimulationRandom.Reset(1);
        Check(SoccerSimulationRandom.Range(0, int.MaxValue) == 270369, "versioned RNG golden value");
        for (var i = 0; i < 1000; i++)
        {
            var value = SoccerSimulationRandom.Range(-3f, 8f);
            Check(value >= -3 && value <= 8, "float bounds");
        }
        Check(SoccerSimulationRandom.Range(4, 4) == 4, "degenerate range");
        var setup = new SoccerMatchSetup
        {
            Seed = -17, ChannelKey = "polbots", HomeActor = "Home", AwayActor = "Away",
            Home = Team("one"), Away = Team("two"), MatchTimeLimit = 10,
            EngineSettingsJson = "{}", UnityVersion = "test", FixedDeltaTime = .02f, TimeScale = 1,
            AiLevel = 4, DayTime = 2
        };
        setup.Validate();
        var directory = Path.Combine("Temp", "SavedMatchTests", Guid.NewGuid().ToString("N"));
        var store = new SoccerMatchStore(directory);
        var original = new SoccerSavedMatch { Id = "original", Setup = setup, Duration = 8, Status = "completed", HomeScore = 2 };
        original.Nodes.Add(new RecordedPlaybackNode
        {
            Offset = 1.25, Sequence = 0, SourceChat = "pregame", Node = JObject.Parse("{\"Actor\":\"Home\",\"Say\":\"\",\"Actions\":[\"wave\"],\"AudioData\":null}")
        });
        original.Nodes.Add(new RecordedPlaybackNode
        {
            Offset = 2, Sequence = 1, SourceChat = "live", Node = JObject.Parse("{\"Actor\":\"Away\",\"Say\":\"hello\",\"AudioData\":\"pcm\",\"Frequency\":48000}")
        });
        store.Save(original);
        var loaded = store.Load("original");
        Check(loaded.Nodes.Count == 2 && loaded.Nodes[0].Node["Actions"][0].Value<string>() == "wave", "silent actions survive reload");
        Check(loaded.Nodes[1].Node["AudioData"].Value<string>() == "pcm" && loaded.Nodes[1].Offset == 2, "spoken audio and timing survive reload");
        Check(loaded.Setup.Home.Players[0]["shooting"].Value<int>() == 75 && loaded.Setup.Home.HomeKit["Color1"]["r"].Value<float>() == .5f, "player and kit settings survive reload");
        loaded.Id = "replay"; loaded.ReplayOf = "original"; loaded.Status = "live"; loaded.Nodes.Clear();
        store.Save(loaded);
        Check(store.Load("original").Nodes.Count == 2 && store.Load("original").HomeScore == 2, "replay cannot overwrite original");
        loaded.Status = "aborted"; store.Save(loaded);
        Check(store.Load("replay").Status == "aborted", "incremental atomic updates preserve aborted runs");
        var originalBytes = File.ReadAllText(Path.Combine(directory, "original.json"));
        foreach (var score in new[] { (home: 2, away: 0, result: "match"), (home: 3, away: 0, result: "mismatch"), (home: 2, away: 1, result: "mismatch") })
        {
            var rerun = new SoccerSavedMatch
            {
                Id = "integrity-" + score.home + "-" + score.away, ReplayOf = original.Id,
                Setup = setup, ReplayIntegrity = SoccerReplayIntegrity.FromOriginal(store.Load(original.Id))
            };
            store.Save(rerun);
            Check(store.Load(rerun.Id).ReplayIntegrity.Result == "pending", "unfinished replay cannot match default scores");
            rerun.CompleteResult(score.home, score.away);
            rerun.FinishRecording(); // Normal teardown must retain the completed comparison.
            store.Save(rerun);
            var result = store.Load(rerun.Id);
            Check(result.Status == "completed" && result.ReplayIntegrity.Result == score.result, "final score comparison survives reload");
            Check(result.HomeScore == score.home && result.AwayScore == score.away &&
                result.ReplayIntegrity.OriginalHomeScore == 2 && result.ReplayIntegrity.OriginalAwayScore == 0,
                "both outcomes preserved");
            Check(result.ReplayDiagnostic.Contains(score.result) && result.ReplayDiagnostic.Contains("original 2-0") &&
                result.ReplayDiagnostic.Contains($"rerun {score.home}-{score.away}"), "operator diagnostics describe both scores");
        }
        var aborted = new SoccerSavedMatch
        {
            Id = "aborted-replay", ReplayOf = original.Id, Setup = setup,
            ReplayIntegrity = SoccerReplayIntegrity.FromOriginal(original), HomeScore = 2
        };
        aborted.FinishRecording(); store.Save(aborted);
        Check(store.Load(aborted.Id).ReplayIntegrity.Result == "aborted" &&
            aborted.ReplayDiagnostic.Contains("no final score"), "aborted playback cannot report a matching score");
        var incompleteOriginal = new SoccerSavedMatch { Status = "aborted" };
        var unverified = new SoccerSavedMatch
        {
            Id = "unverified", Setup = setup, ReplayOf = "incomplete",
            ReplayIntegrity = SoccerReplayIntegrity.FromOriginal(incompleteOriginal)
        };
        unverified.CompleteResult(0, 0); store.Save(unverified);
        Check(store.Load(unverified.Id).ReplayIntegrity.Result == "unavailable", "incomplete original cannot verify a completed rerun");
        Check(store.Load(original.Id).ReplayIntegrity == null, "legacy/original records have no implied verification");
        Check(File.ReadAllText(Path.Combine(directory, "original.json")) == originalBytes, "all replay outcomes leave original bytes untouched");
        Reject(() => store.Load("../escape"), "path traversal accepted");
        setup.Version = 99; Reject(setup.Validate, "future schema accepted"); setup.Version = 1;
        setup.RandomAlgorithm = "other"; Reject(setup.Validate, "unknown RNG accepted"); setup.RandomAlgorithm = SoccerSimulationRandom.Algorithm;
        setup.TimeScale = float.NaN; Reject(setup.Validate, "invalid clock accepted"); setup.TimeScale = 1;
        setup.Home.Players = new JObject[0]; Reject(setup.Validate, "missing roster accepted");
        Console.WriteLine("Saved match setup, RNG, timeline, and persistence checks passed.");
    }
    private static SoccerTeamSnapshot Team(string name) => new SoccerTeamSnapshot
    {
        Template = name, Name = name, Formation = 2,
        Players = Enumerable.Range(0, 11).Select(i => JObject.FromObject(new { Name = "Player " + i, shooting = 75 })).ToArray(),
        HomeKit = JObject.Parse("{\"Color1\":{\"r\":0.5,\"g\":0.2,\"b\":0.1,\"a\":1}}"), AwayKit = new JObject(), Logo = new JObject()
    };
}
