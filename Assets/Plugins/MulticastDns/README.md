# Multicast DNS dependencies

Pinned upstream NuGet binaries for the control panel's managed mDNS responder. No Bonjour installation or native service is required. These assemblies are included in Unity player builds.

| Package | Version | Assembly path in NuGet package | License |
| --- | --- | --- | --- |
| [Makaretu.Dns.Multicast](https://www.nuget.org/packages/Makaretu.Dns.Multicast/0.27.0) | 0.27.0 | lib/netstandard1.4/Makaretu.Dns.Multicast.dll | MIT, net-mdns-LICENSE.txt |
| [Makaretu.Dns](https://www.nuget.org/packages/Makaretu.Dns/2.0.1) | 2.0.1 | lib/netstandard2.0/Makaretu.Dns.dll | MIT, net-dns-LICENSE.txt |
| [Common.Logging](https://www.nuget.org/packages/Common.Logging/3.4.1) | 3.4.1 | lib/netstandard1.3/Common.Logging.dll | Apache-2.0, Common.Logging-LICENSE.txt |
| [Common.Logging.Core](https://www.nuget.org/packages/Common.Logging.Core/3.4.1) | 3.4.1 | lib/netstandard1.0/Common.Logging.Core.dll | Apache-2.0, Common.Logging-LICENSE.txt |
| [IPNetwork2](https://www.nuget.org/packages/IPNetwork2/2.1.2) | 2.1.2 | lib/netstandard1.3/System.Net.IPNetwork.dll | BSD-2-Clause, IPNetwork2-LICENSE.txt |
| [SimpleBase](https://www.nuget.org/packages/SimpleBase/1.3.1) | 1.3.1 | lib/netstandard1.3/SimpleBase.dll | Apache-2.0, SimpleBase-LICENSE.txt |

To reproduce, download each pinned `.nupkg` from NuGet's flat-container API, extract it as a ZIP, and copy the listed assembly. For example: `https://api.nuget.org/v3-flatcontainer/makaretu.dns.multicast/0.27.0/makaretu.dns.multicast.0.27.0.nupkg`.

The multicast assembly uses its .NET Standard 1.4 build, which relies on managed sockets and avoids the newer build's Linux-specific Tmds.LibC dependency. Unity supplies the framework assemblies. License texts are bundled alongside the binaries; retain them when redistributing. SimpleBase copyright: 2014-2017 Sedat Kapanoglu.
