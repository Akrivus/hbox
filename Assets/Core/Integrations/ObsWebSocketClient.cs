using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>OBS WebSocket v5 with ordered handshake and acknowledged responses.</summary>
public sealed class ObsWebSocketClient
{
    private readonly string uri;
    private readonly string password;
    private readonly TimeSpan timeout;

    public ObsWebSocketClient(string uri, string password = null, TimeSpan? timeout = null)
    {
        this.uri = uri;
        this.password = password;
        this.timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<JObject> RequestAsync(string requestType)
    {
        using (var deadline = new CancellationTokenSource(timeout))
        using (var socket = new ClientWebSocket())
        {
            var token = deadline.Token;
            await socket.ConnectAsync(new Uri(uri), token);
            var hello = await ReceiveAsync(socket, token);
            if (hello.Value<int>("op") != 0) throw new IOException("OBS did not send Hello.");
            var identify = new JObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
            var auth = hello["d"]?["authentication"];
            if (auth != null)
            {
                if (string.IsNullOrEmpty(password))
                    throw new IOException("OBS requires authentication. Set obs.OBSWebSocketPassword.");
                identify["authentication"] = Hash(Hash(password + (string)auth["salt"]) + (string)auth["challenge"]);
            }
            await SendAsync(socket, new JObject { ["op"] = 1, ["d"] = identify }, token);
            var identified = await ReceiveAsync(socket, token);
            if (identified.Value<int>("op") != 2) throw new IOException("OBS did not acknowledge Identify.");
            var id = Guid.NewGuid().ToString("N");
            await SendAsync(socket, new JObject
            {
                ["op"] = 6,
                ["d"] = new JObject { ["requestType"] = requestType, ["requestId"] = id }
            }, token);
            while (true)
            {
                var message = await ReceiveAsync(socket, token);
                var data = message["d"];
                if (message.Value<int>("op") != 7 || (string)data?["requestId"] != id) continue;
                if ((bool?)data?["requestStatus"]?["result"] != true)
                    throw new IOException($"OBS rejected {requestType} ({data?["requestStatus"]?["code"]}): {data?["requestStatus"]?["comment"]}");
                return data["responseData"] as JObject ?? new JObject();
            }
        }
    }

    private static string Hash(string value)
    {
        using (var sha = SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static Task SendAsync(ClientWebSocket socket, JObject message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private static async Task<JObject> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        using (var message = new MemoryStream())
        {
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new IOException("OBS closed the connection or sent a non-text message.");
                message.Write(buffer, 0, result.Count);
                if (message.Length > 1024 * 1024) throw new IOException("OBS response exceeds 1 MB.");
            } while (!result.EndOfMessage);
            return JObject.Parse(Encoding.UTF8.GetString(message.ToArray()));
        }
    }
}
