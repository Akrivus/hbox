using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static class RecordingTests
{
    private static int passed;
    private static readonly string Root = Path.GetFullPath(Path.Combine("Temp", "RecordingTests", Guid.NewGuid().ToString("N")));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static async Task Throws(Func<Task> action)
    {
        try { await action(); } catch { return; }
        throw new Exception("Expected failure");
    }
    private static RecordedEpisode Episode(string slug, string channel = "polbots") => new RecordedEpisode
    { channelKey = channel, slug = slug, title = slug };
    private static RecordingCatalog Catalog(string name) => new RecordingCatalog(Path.Combine(Root, name));
    private static async Task Test(string name, Func<Task> test)
    {
        await test();
        Console.WriteLine("PASS " + name);
        passed++;
    }
    private sealed class FakeObs
    {
        public bool active;
        public int stops;
        public string fail;
        public string path = "D:/OBS/arbitrary-recording-name.mkv";
        public List<string> commands = new List<string>();
        public Task<JObject> Send(string command)
        {
            commands.Add(command);
            if (command == fail) throw new IOException("Simulated OBS failure");
            switch (command)
            {
                case "GetRecordStatus": return Task.FromResult(new JObject { ["outputActive"] = active });
                case "StartRecord": active = true; break;
                case "StopRecord": active = false; stops++; return Task.FromResult(new JObject { ["outputPath"] = path });
            }
            return Task.FromResult(new JObject());
        }
    }
    public static async Task<int> Main()
    {
        try { await Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Run()
    {
        await Test("split binds OBS output to outgoing episode, not incoming identity", async () =>
        {
            var catalog = Catalog("split"); var obs = new FakeObs(); var session = new ObsRecordingSession(catalog);
            var a = Episode("a");
            await session.BeginAsync(a, true, obs.Send);
            a.slug = "changed-now-playing";
            await session.BeginAsync(Episode("b"), true, obs.Send);
            var record = catalog.Find("polbots", "a").Single();
            Check(record.outputPath == obs.path && record.status == "completed", "Outgoing episode mapping");
            Check(catalog.Find("polbots", "b").Single().outputPath == null, "Incoming must not acquire previous file");
            obs.path = "D:/OBS/second.mp4"; await session.StopAsync();
            Check(catalog.Find("polbots", "b").Single().outputPath == obs.path, "Second episode mapping");
            Check(obs.commands.SequenceEqual(new[] { "GetRecordStatus", "StartRecord", "StopRecord", "GetRecordStatus", "StartRecord", "StopRecord" }), "Command ordering");
        });
        await Test("continuous recording records every episode and survives catalog reload", async () =>
        {
            var catalog = Catalog("continuous"); var obs = new FakeObs(); var session = new ObsRecordingSession(catalog);
            await session.BeginAsync(Episode("a"), false, obs.Send);
            await session.BeginAsync(Episode("b"), false, obs.Send);
            await session.StopAsync(); await session.StopAsync();
            var reloaded = Catalog("continuous");
            Check(reloaded.Find("polbots", "a").Single().episodes.Count == 2, "Shared recording lost episodes");
            Check(reloaded.Find("polbots", "b").Single().outputPath == obs.path && obs.stops == 1, "Idempotent stop");
        });
        await Test("takes accumulate and identical slugs in different channels stay separate", async () =>
        {
            var catalog = Catalog("takes"); var obs = new FakeObs(); var session = new ObsRecordingSession(catalog);
            await session.BeginAsync(Episode("a"), true, obs.Send); await session.StopAsync();
            await session.BeginAsync(Episode("a"), true, obs.Send); await session.StopAsync();
            await session.BeginAsync(Episode("a", "romebots"), true, obs.Send); await session.StopAsync();
            Check(catalog.Find("polbots", "a").Count == 2 && catalog.Find("romebots", "a").Count == 1, "Identity collision");
        });
        await Test("existing external recording is never adopted or stopped", async () =>
        {
            var catalog = Catalog("external"); var obs = new FakeObs { active = true }; var session = new ObsRecordingSession(catalog);
            await Throws(() => session.BeginAsync(Episode("a"), true, obs.Send));
            await session.StopAsync();
            Check(obs.stops == 0 && catalog.Find("polbots", "a").Count == 0, "Adopted external recording");
        });
        await Test("start failure remains unconfirmed with no invented file", async () =>
        {
            var catalog = Catalog("startfail"); var obs = new FakeObs { fail = "StartRecord" }; var session = new ObsRecordingSession(catalog);
            await Throws(() => session.BeginAsync(Episode("a"), true, obs.Send));
            await session.StopAsync();
            var r = catalog.Find("polbots", "a").Single();
            Check(r.status == "unconfirmed" && r.outputPath == null && obs.stops == 0, "Incorrect success on start failure");
        });
        await Test("stop failure never assigns a guessed path or retries a mutation", async () =>
        {
            var catalog = Catalog("stopfail"); var obs = new FakeObs(); var session = new ObsRecordingSession(catalog);
            await session.BeginAsync(Episode("a"), true, obs.Send); obs.fail = "StopRecord";
            await Throws(() => session.StopAsync()); await session.StopAsync();
            var r = catalog.Find("polbots", "a").Single();
            Check(r.status == "unconfirmed" && r.outputPath == null, "Guessed path");
            Check(obs.commands.Count(c => c == "StopRecord") == 1, "Retried ambiguous stop");
        });
        await Test("missing StopRecord outputPath is not marked completed", async () =>
        {
            var catalog = Catalog("missing"); var obs = new FakeObs { path = null }; var session = new ObsRecordingSession(catalog);
            await session.BeginAsync(Episode("a"), true, obs.Send); await Throws(() => session.StopAsync());
            Check(catalog.Find("polbots", "a").Single().status == "unconfirmed", "Missing path accepted");
        });
        await Test("failed final catalog write retries persistence without stopping again", async () =>
        {
            var catalog = Catalog("writefail"); var obs = new FakeObs(); var session = new ObsRecordingSession(catalog);
            await session.BeginAsync(Episode("a"), true, obs.Send);
            var id = catalog.Find("polbots", "a").Single().id;
            var blocker = Path.Combine(Root, "writefail", id + ".json.tmp");
            Directory.CreateDirectory(blocker);
            await Throws(() => session.StopAsync());
            Directory.Delete(blocker);
            await session.StopAsync();
            Check(obs.stops == 1 && catalog.Find("polbots", "a").Single().status == "completed", "Persistence retry stopped another recording");
        });
        await Test("continuous mode detects external stop", async () =>
        {
            var catalog = Catalog("externalstop"); var obs = new FakeObs(); var session = new ObsRecordingSession(catalog);
            await session.BeginAsync(Episode("a"), false, obs.Send); obs.active = false;
            await Throws(() => session.BeginAsync(Episode("b"), false, obs.Send));
            Check(catalog.Find("polbots", "b").Count == 0 && catalog.Find("polbots", "a").Single().status == "unconfirmed", "Attached absent recording");
        });
        await Test("websocket authenticates, handles fragments and correlates responses", () => WebSocketCase("success"));
        await Test("websocket rejection propagates", () => WebSocketCase("reject"));
        await Test("websocket missing reply times out", () => WebSocketCase("timeout"));
        Console.WriteLine($"{passed} regression tests passed.");
    }

    private static async Task WebSocketCase(string mode)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var stream = connection.GetStream();
            var header = new StringBuilder(); var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n"))
            {
                Check(await stream.ReadAsync(one, 0, 1) == 1, "Missing websocket upgrade");
                header.Append((char)one[0]);
            }
            var key = header.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var upgrade = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
            await stream.WriteAsync(upgrade);
            using var socket = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30));
            await Send(socket, new JObject { ["op"] = 0, ["d"] = new JObject { ["authentication"] = new JObject { ["salt"] = "salt", ["challenge"] = "challenge" } } }, true);
            var identify = await Receive(socket);
            string Hash(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
            Check((string)identify["d"]["authentication"] == Hash(Hash("passwordsalt") + "challenge"), "Authentication mismatch");
            await Send(socket, new JObject { ["op"] = 2, ["d"] = new JObject() });
            var request = await Receive(socket);
            if (mode == "timeout") { await Task.Delay(3000); return; }
            JObject Reply(string id) => new JObject { ["op"] = 7, ["d"] = new JObject
            { ["requestId"] = id, ["requestStatus"] = new JObject { ["result"] = mode != "reject", ["code"] = mode == "reject" ? 500 : 100 }, ["responseData"] = new JObject { ["outputPath"] = "exact-file.mkv" } } };
            await Send(socket, Reply("wrong-id"));
            await Send(socket, Reply((string)request["d"]["requestId"]), true);
        });
        var client = new ObsWebSocketClient($"ws://127.0.0.1:{port}/", "password", TimeSpan.FromMilliseconds(mode == "timeout" ? 1500 : 5000));
        if (mode == "success") Check((await client.RequestAsync("StopRecord")).Value<string>("outputPath") == "exact-file.mkv", "Incorrect response");
        else await Throws(() => client.RequestAsync("StopRecord"));
        await server.WaitAsync(TimeSpan.FromSeconds(8));
    }
    private static async Task Send(WebSocket socket, JObject obj, bool fragment = false)
    {
        var bytes = Encoding.UTF8.GetBytes(obj.ToString());
        var first = fragment ? bytes.Length / 2 : bytes.Length;
        await socket.SendAsync(new ArraySegment<byte>(bytes, 0, first), WebSocketMessageType.Text, !fragment, CancellationToken.None);
        if (fragment) await socket.SendAsync(new ArraySegment<byte>(bytes, first, bytes.Length - first), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    private static async Task<JObject> Receive(WebSocket socket)
    {
        var bytes = new byte[4096]; var result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), CancellationToken.None);
        return JObject.Parse(Encoding.UTF8.GetString(bytes, 0, result.Count));
    }
}
