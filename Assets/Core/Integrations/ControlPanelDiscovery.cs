using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;

// Owns its sockets on the server worker task. Cancellation withdraws records and closes them.
internal static class ControlPanelDiscovery
{
    internal static ServiceProfile CreateProfile(string hostName, int port, IPAddress address)
    {
        var host = new DomainName(hostName + ".local");
        var profile = new ServiceProfile
        {
            InstanceName = new DomainName(hostName),
            ServiceName = new DomainName("_http._tcp"),
            HostName = host
        };
        profile.Resources.Add(new SRVRecord
        {
            Name = profile.FullyQualifiedName, Target = host, Port = (ushort)port
        });
        profile.Resources.Add(new TXTRecord { Name = profile.FullyQualifiedName, Strings = { "path=/" } });
        profile.Resources.Add(new ARecord { Name = host, Address = address });
        return profile;
    }

    internal static bool HasConflict(Message message, ServiceProfile profile, IPAddress address)
    {
        return message.Answers.Concat(message.AdditionalRecords).Concat(message.AuthorityRecords)
            .Any(record => record.TTL > TimeSpan.Zero &&
                ((record is AddressRecord a && a.Name == profile.HostName && !a.Address.Equals(address)) ||
                 (record is SRVRecord s && s.Name == profile.FullyQualifiedName &&
                  (s.Target != profile.HostName || s.Port != profile.Resources.OfType<SRVRecord>().Single().Port))));
    }

    public static async Task RunAsync(ServerConfigs config, IPAddress address,
        CancellationToken token, Action<string> log, Action<string> warn)
    {
        // Match the HTTP listener's selected IPv4 interface; do not announce an unreachable address.
        using (var mdns = new MulticastService(interfaces => interfaces.Where(nic =>
                   nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address))))
               { UseIpv4 = true, UseIpv6 = false })
        using (var discovery = new ServiceDiscovery(mdns))
        {
            var profile = CreateProfile(config.HostName, config.Port, address);
            var conflict = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<MessageEventArgs> check = (sender, args) =>
            {
                if (HasConflict(args.Message, profile, address)) conflict.TrySetResult(true);
            };
            mdns.AnswerReceived += check;
            mdns.QueryReceived += check;
            var advertised = false;
            try
            {
                mdns.Start();
                // Probe before claiming a name. Simultaneous conflicting probes yield safely;
                // the operator can assign distinct HostName values for machines on the same LAN.
                await Task.Delay(new Random().Next(0, 251), token).ConfigureAwait(false);
                for (var i = 0; i < 3 && !conflict.Task.IsCompleted; i++)
                {
                    var probe = new Message();
                    probe.Questions.Add(new Question { Name = profile.HostName, Type = DnsType.ANY });
                    probe.Questions.Add(new Question { Name = profile.FullyQualifiedName, Type = DnsType.ANY });
                    probe.AuthorityRecords.AddRange(profile.Resources.Where(r => r is ARecord || r is SRVRecord));
                    mdns.SendQuery(probe);
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                if (!conflict.Task.IsCompleted)
                {
                    discovery.Advertise(profile);
                    advertised = true;
                    discovery.Announce(profile);
                    log($"Control panel advertised at {config.MdnsUrl}");
                    await Task.WhenAny(conflict.Task, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
                }
                if (conflict.Task.IsCompleted && !token.IsCancellationRequested)
                    warn($"mDNS name {config.HostName}.local is already in use. Discovery stopped; set a unique server HostName in hbox.json and restart. HTTP remains available at {config.LocalUrl} or {address}:{config.Port}.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                mdns.AnswerReceived -= check;
                mdns.QueryReceived -= check;
                if (advertised) discovery.Unadvertise(profile);
            }
        }
    }
}
