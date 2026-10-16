using System;
using System.Linq;
using FStudio.Database;
using UnityEngine;

static class SetupSnapshotTests
{
    public static void Run()
    {
        var original = new TeamEntry
        {
            name = "template", TeamName = "original", Formation = FStudio.Data.Formations.Two,
            HomeKit = new KitEntry { Color1 = new Color { r = .5f, a = 1 }, KitMaterial = new Texture { name = "stripes" } },
            AwayKit = new KitEntry(), TeamLogo = new LogoEntry(),
            Players = Enumerable.Range(0, 11).Select(i => new PlayerEntry { Name = "P" + i, shooting = 75 }).ToArray()
        };
        var copy = SoccerSetupSnapshots.Clone(original);
        copy.HomeKit.Color1 = new Color { r = 1 };
        copy.Players[0].Name = "captured";
        if (original.HomeKit.Color1.r != .5f || original.Players[0].Name != "P0")
            throw new Exception("Cloning must isolate vendor kit/player assets.");
        var snapshot = SoccerSetupSnapshots.Capture(copy);
        var restored = SoccerSetupSnapshots.Restore(snapshot, new[] { original });
        if (restored.HomeKit.Color1.r != 1 || restored.Players[0].Name != "captured" || restored.Players[0].shooting != 75)
            throw new Exception("Full setup did not restore.");
        original.HomeKit.KitMaterial.name = "different";
        try { SoccerSetupSnapshots.Restore(snapshot, new[] { original }); }
        catch (FormatException) { Console.WriteLine("Team clone/restore/asset compatibility checks passed."); return; }
        throw new Exception("Changed kit dependency accepted.");
    }
}

// Minimal Unity/vendor asset boundaries. The production clone/snapshot/restore code runs above.
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static T Instantiate<T>(T value) where T : Object => (T)value.MemberwiseClone();
        public static void Destroy(Object value) { }
    }
    public class Texture : Object { }
    public struct Color { public float r, g, b, a; }
}
namespace FStudio.Data { public enum Formations { One, Two } }
namespace FStudio.Database
{
    public class TeamEntry : UnityEngine.Object
    {
        public string TeamName;
        public FStudio.Data.Formations Formation;
        public PlayerEntry[] Players;
        public KitEntry HomeKit, AwayKit;
        public LogoEntry TeamLogo;
        public object Clone() => new TeamEntry
        {
            TeamName = TeamName, Formation = Formation, Players = Players.Select(p => new PlayerEntry { Name = p.Name, shooting = p.shooting }).ToArray(),
            HomeKit = HomeKit, AwayKit = AwayKit, TeamLogo = TeamLogo
        };
    }
    public class PlayerEntry : UnityEngine.Object { public TeamEntry team; public string Name; public int shooting; }
    public class KitEntry : UnityEngine.Object { public UnityEngine.Texture KitMaterial; public UnityEngine.Color Color1; }
    public class LogoEntry : UnityEngine.Object { public UnityEngine.Color TeamLogoColor1; }
}
