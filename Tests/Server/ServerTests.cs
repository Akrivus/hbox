using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;

internal static class ServerTests
{
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
    }

    private static async Task Main(string[] args)
    {
        var config = new ServerConfigs { Port = 80 };
        config.Validate();
        Check(config.MdnsUrl == "http://hbox.local", "Port 80 URL");
        Check(config.LocalUrl == "http://localhost", "Port 80 local URL");
        config.Port = 6789;
        Check(config.MdnsUrl == "http://hbox.local:6789", "Custom port URL");
        foreach (var invalid in new[] { "", "hbox.local", "-hbox", "hbox-", "a/b", new string('a', 64) })
        {
            config.HostName = invalid;
            try { config.Validate(); throw new Exception("Accepted invalid hostname " + invalid); }
            catch (ArgumentException) { }
        }
        config.HostName = "hbox-studio";
        foreach (var invalid in new[] { 0, -1, 65536 })
        {
            config.Port = invalid;
            try { config.Validate(); throw new Exception("Accepted invalid port"); }
            catch (ArgumentException) { }
        }

        var address = IPAddress.Parse("192.0.2.10");
        var profile = ControlPanelDiscovery.CreateProfile("hbox", 80, address);
        Check(profile.HostName == new DomainName("hbox.local"), "Exact hostname, without service suffix");
        Check(profile.Resources.OfType<SRVRecord>().Single().Target == profile.HostName, "SRV target");
        using (var mdns = new MulticastService())
        using (var discovery = new ServiceDiscovery(mdns))
        {
            discovery.Advertise(profile);
            var question = new Message();
            question.Questions.Add(new Question { Name = "hbox.local", Type = DnsType.A });
            var answer = await discovery.NameServer.ResolveAsync(question);
            Check(answer.Answers.OfType<ARecord>().Any(a => a.Address.Equals(address)), "Hostname resolves to LAN address");
            question = new Message();
            question.Questions.Add(new Question { Name = "_http._tcp.local", Type = DnsType.PTR });
            answer = await discovery.NameServer.ResolveAsync(question);
            Check(answer.Answers.OfType<PTRRecord>().Any(p => p.DomainName == profile.FullyQualifiedName), "HTTP service discovery");
        }

        var message = new Message();
        message.Answers.Add(new ARecord { Name = "hbox.local", Address = address });
        Check(!ControlPanelDiscovery.HasConflict(message, profile, address), "Own answers do not conflict");
        message.Answers.Add(new ARecord { Name = "hbox.local", Address = IPAddress.Parse("192.0.2.11") });
        Check(ControlPanelDiscovery.HasConflict(message, profile, address), "Duplicate hostname detected");
        message.Answers.Last().TTL = TimeSpan.Zero;
        Check(!ControlPanelDiscovery.HasConflict(message, profile, address), "Goodbye does not conflict");
        message.AuthorityRecords.Add(new ARecord { Name = "hbox.local", Address = IPAddress.Parse("192.0.2.12") });
        Check(ControlPanelDiscovery.HasConflict(message, profile, address), "Simultaneous probe detected");
        message = new Message();
        message.AdditionalRecords.Add(new SRVRecord { Name = profile.FullyQualifiedName, Target = profile.HostName, Port = 6789 });
        Check(ControlPanelDiscovery.HasConflict(message, profile, address), "Duplicate service with different port detected");
        Console.WriteLine("PASS: config validation, URLs, hostname resolution, HTTP service records, conflict detection.");

        if (args.Contains("--network"))
        {
            await NetworkTest(false);
            await NetworkTest(true);
        }
    }

    private static async Task NetworkTest(bool injectConflict)
    {
        var address = MulticastService.GetLinkLocalAddresses().First(a => a.AddressFamily == AddressFamily.InterNetwork);
        var config = new ServerConfigs { HostName = "hbox-test-" + Guid.NewGuid().ToString("N").Substring(0, 8), Port = 18080 };
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
        {
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var conflictDetected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = ControlPanelDiscovery.RunAsync(config, address, stop.Token,
                text => ready.TrySetResult(true), text => conflictDetected.TrySetResult(true));
            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(6));
                using (var client = new UdpClient(new IPEndPoint(address, 0)))
                {
                    var query = new Message { Id = 42 };
                    query.Questions.Add(new Question { Name = config.HostName + ".local", Type = DnsType.A });
                    var bytes = query.ToByteArray();
                    await client.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                    var result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(4));
                    var answer = new Message();
                    answer.Read(result.Buffer);
                    Check(answer.Answers.OfType<ARecord>().Any(a => a.Name == new DomainName(config.HostName + ".local") && a.Address.Equals(address)), "Live mDNS answer");
                }
                if (injectConflict)
                {
                    using (var rival = new MulticastService { UseIpv6 = false })
                    {
                        rival.Start();
                        var conflict = new Message { QR = true };
                        conflict.Answers.Add(new ARecord { Name = config.HostName + ".local", Address = IPAddress.Parse("192.0.2.123") });
                        rival.SendAnswer(conflict);
                        await conflictDetected.Task.WaitAsync(TimeSpan.FromSeconds(3));
                        await running.WaitAsync(TimeSpan.FromSeconds(3));
                    }
                }
            }
            finally
            {
                stop.Cancel();
                await running.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
        Console.WriteLine(injectConflict ? "PASS: live duplicate hostname withdraws advertisement." : "PASS: live UDP mDNS hostname query and shutdown.");
    }
}
