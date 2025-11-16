using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class OBS : MonoBehaviour, IConfigurable<OBSConfigs>
{
    [SerializeField]
    private string VideosFolder;
    [SerializeField]
    private string OBSWebSocketURI = "ws://localhost:4455";
    [SerializeField]
    private bool IsStreaming = false;
    [SerializeField]
    private bool IsRecording = false;
    [SerializeField]
    private bool DoSplitRecording = false;
    [SerializeField]
    private bool OnlyNewEpisodes = true;

    private bool isObsRecording = false;
    private bool isObsStreaming = false;

    public static string ProductionCode = null;

    public void Configure(OBSConfigs c)
    {
        VideosFolder = c.VideosFolder;
        OBSWebSocketURI = c.OBSWebSocketURI;
        IsStreaming = c.IsStreaming;
        IsRecording = c.IsRecording;
        DoSplitRecording = c.DoSplitRecording;
        OnlyNewEpisodes = c.OnlyNewEpisodes;

        if (IsRecording)
            ChatManager.Instance.AfterIntermission += StopOrStartRecording;
        if (DoSplitRecording)
            ChatManager.Instance.BeforeIntermission += SplitRecording;

        ChatManager.Instance.OnChatQueueEmpty += StopRecording;

        if (IsStreaming)
            StartStreaming();
    }

    private void Awake()
    {
        ConfigManager.Instance.RegisterConfig(typeof(OBSConfigs), "obs", (config) => Configure((OBSConfigs)config));
    }

    private void OnDestroy()
    {
        if (IsStreaming)
            StopStreaming();
        if (IsRecording)
            StopRecording();
    }

    public async void StartRecording()
    {
        if (isObsRecording)
            return;
        isObsRecording = true;
        await SendRequestAsync("StartRecord");
    }

    public async void StopRecording()
    {
        if (!isObsRecording)
            return;
        isObsRecording = false;
        await SendRequestAsync("StopRecord");
        await WaitForVideoFile();
    }

    public void StopOrStartRecording(Chat chat)
    {
        if (chat.NewEpisode && OnlyNewEpisodes)
            StartRecording();
        else if (OnlyNewEpisodes)
            StopRecording();
        else if (!isObsRecording)
            StartRecording();
    }

    public async void SplitRecording()
    {
        if (!isObsRecording)
            return;
        await SendRequestAsync("SplitRecordFile");
        await WaitForVideoFile();
    }

    public async void StartStreaming()
    {
        if (isObsStreaming)
            return;
        isObsStreaming = true;
        await SendRequestAsync("StartStreaming");
    }

    public async void StopStreaming()
    {
        if (!isObsStreaming)
            return;
        isObsStreaming = false;
        await SendRequestAsync("StopStreaming");
    }

    public async Task SendRequestAsync(string requestType, int attempts = 0)
    {
        using (var client = new ClientWebSocket())
        {
            try
            {
                await ConnectAsync(client);

                if (client.State.HasFlag(WebSocketState.Open))
                    await SendAsync(client, new Message<Request<object>>(6, new Request<object>(requestType)));
            }
            catch (WebSocketException e)
            {
                Debug.LogError(e);
                if (attempts > 10)
                    return;
                await SendRequestAsync(requestType, ++attempts);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
    }

    private async Task SendAsync<T>(ClientWebSocket client, Message<T> m)
    {
        await SendStringAsync(client, JsonConvert.SerializeObject(m));
    }

    private async Task SendStringAsync(ClientWebSocket client, string message)
    {
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
        await client.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<string> ReceiveAsync(ClientWebSocket client, int bufferSize = 1024)
    {
        var buffer = new ArraySegment<byte>(new byte[bufferSize]);
        var result = await client.ReceiveAsync(buffer, CancellationToken.None);
        return Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
    }

    private async Task ConnectAsync(ClientWebSocket client)
    {
        await client.ConnectAsync(new Uri(OBSWebSocketURI), CancellationToken.None)
            .ContinueWith(async (_) => await ReceiveAsync(client))
            .ContinueWith(async (_) => await SendAsync(client, new Message<Handshake>(1, new Handshake())))
            .ContinueWith(async (_) => await ReceiveAsync(client));
    }

    private async Task WaitForVideoFile(int attempts = 0)
    {
        if (attempts > 60)
            return;
        try
        {
            var files = Directory.GetFiles(VideosFolder, "*.mkv");
            var latest = files.OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
            if (latest == null)
                throw new Exception();
            var fileName = Path.GetFileNameWithoutExtension(latest);
            if (fileName.Length == "1234-12-12 12-12-12".Length)
            {
                var inst = ChatManager.Instance;
                var newName = $"{fileName}-{inst.NowPlaying.FileName}.mkv";
                var newPath = Path.Combine(VideosFolder, newName);

                if (File.Exists(newPath))
                    return;
                File.Move(latest, newPath);
            }
        }
        catch
        {
            await Task.Delay(1000);
            await WaitForVideoFile(++attempts);
        }
    }

    private class Message<T>
    {
        public int op { get; set; }
        public T d { get; set; }

        public Message(int op, T d)
        {
            this.op = op;
            this.d = d;
        }
    }

    private class Request<T>
    {
        public string requestType { get; set; }
        public string requestId { get; set; } = Guid.NewGuid().ToString();
        public T requestData { get; set; }

        public Request(string requestType, T requestData)
        {
            this.requestType = requestType;
            this.requestData = requestData;
        }

        public Request(string requestType)
        {
            this.requestType = requestType;
        }

        public bool ShouldSerializeData()
        {
            return requestData != null;
        }
    }

    private class Handshake
    {
        public int rpcVersion = 1;
    }
}