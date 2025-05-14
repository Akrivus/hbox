
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ServerSource : MonoBehaviour, IConfigurable<ServerConfigs>
{
    public static ServerSource Instance { get; private set; }

    static Dictionary<string, Dictionary<string, Action<HttpListenerContext>>> routes = new Dictionary<string, Dictionary<string, Action<HttpListenerContext>>>()
    {
        { "GET",    new Dictionary<string, Action<HttpListenerContext>>() },
        { "POST",   new Dictionary<string, Action<HttpListenerContext>>() },
        { "PUT",    new Dictionary<string, Action<HttpListenerContext>>() },
        { "PATCH",  new Dictionary<string, Action<HttpListenerContext>>() },
        { "DELETE", new Dictionary<string, Action<HttpListenerContext>>() },
    };

    private HttpListener listener;
    private Thread thread;

    private CancellationTokenSource cts = new CancellationTokenSource();
    private CancellationToken token;

    [SerializeField]
    private ChatGenerator generator;

    public bool IsListening { get; private set; } = true;

    public void Configure(ServerConfigs c)
    {
        for (var i = 0; i < c.Prompts.Count; i++)
            if (File.Exists(c.Prompts[i]))
                c.Prompts[i] = File.ReadAllText(c.Prompts[i]);
        if (c.Prompts.Count > 0)
            StartCoroutine(GeneratePrompts(c.Prompts));
    }

    private IEnumerator GeneratePrompts(List<string> prompts)
    {
        foreach (var prompt in prompts)
            yield return generator.GenerateAndPlay(new Idea(prompt)).AsCoroutine();
    }

    public void Awake()
    {
        if (Instance != null)
            Debug.LogWarning("Multiple ServerIntegrations found, this is not good.");
        Instance = this;

        ConfigManager.Instance.RegisterConfig(typeof(ServerConfigs), "human", (config) => Configure((ServerConfigs) config));

        AddRoute("POST", $"/generate", (_) => ProcessBodyString(_, s => generator.AddPromptToQueue(s)));
        AddRoute("GET", "/", (_) => ProcessFileRequest(_, "index.html"));
    }

    private void Start()
    {
        token = cts.Token;
        listener = new HttpListener();
        listener.Prefixes.Add($"http://{GetLocalIPAddress()}:8080/");
        listener.Prefixes.Add($"http://localhost:8080/");
        thread = new Thread(Listen);
        thread.Start();
    }

    private void OnApplicationQuit()
    {
        if (listener != null)
            listener.Stop();
        cts.Cancel();
        IsListening = false;
        StopAllCoroutines();
    }

    private async void Listen()
    {
        listener.Start();
        while (!token.IsCancellationRequested && listener.IsListening && IsListening)
            ProcessRequest(await listener.GetContextAsync());
        listener.Close();
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.KeepAlive = false;
        response.StatusCode = 200;

        var method = request.HttpMethod;
        var path = request.Url.AbsolutePath;
        var query = request.Url.Query;

        try
        {
            if (routes.ContainsKey(method))
                if (routes[method].ContainsKey(path))
                    routes[method][path](context);
                else
                    response.StatusCode = 404;
            else
                response.StatusCode = 405;
        }
        catch (Exception e)
        {
            var json = JsonConvert.SerializeObject(e);
            response.WriteString(json, "application/json");
            response.StatusCode = 500;
        }
        response.Close();
    }

    public static void ProcessFileRequest(HttpListenerContext context, string path)
    {
        var file = Path.Combine(Application.streamingAssetsPath, path);
        var text = File.ReadAllText(file);

        context.Response.WriteString(text, "text/html");
    }

    public static void ProcessBodyString(HttpListenerContext context, Action<string> handler)
    {
        var req = context.Request;
        var res = context.Response;
        var body = new byte[req.ContentLength64];

        req.InputStream.Read(body, 0, body.Length);

        var text = Encoding.UTF8.GetString(body);

        handler(text);
        res.WriteString("OK", "application/text");
    }

    public static void Register(string method, string path, Action<HttpListenerContext> handler)
    {
        if (!routes.ContainsKey(method))
            throw new ArgumentException("Invalid method: " + method);
        routes[method][path] = handler;
    }

    public static void AddRoute(string method, string path, Action<HttpListenerContext> handler)
    {
        Register(method, path, handler);
    }

    public static void AddRoute(string method, string path, Func<HttpListenerContext, Task> handler)
    {
        Register(method, path, async context => await handler(context));
    }

    public static void AddRoute(string method, string path, Func<string, Task<string>> handler)
    {
        Register(method, path, async context => await Route(context, handler));
    }

    public static void AddApiRoute<I, O>(string method, string path, Func<I, Task<O>> handler)
    {
        Register(method, path, async context => await ApiRoute(context, handler));
    }

    public static void AddApiRoute<O>(string method, string path, Func<Task<O>> handler)
    {
        Register(method, path, async context => await ApiRoute(context, handler));
    }

    public static void AddGetRoute(string path, Action<Dictionary<string, string>, HttpListenerResponse> handler)
    {
        AddRoute("GET", path, context => GetRoute(context, handler));
    }

    public static async Task Route(HttpListenerContext context, Func<string, Task<string>> route, string contentType = "application/text")
    {
        var req = context.Request;
        var res = context.Response;
        var body = new byte[req.ContentLength64];

        req.InputStream.Read(body, 0, body.Length);

        var text = Encoding.UTF8.GetString(body);
        res.WriteString(await route(text), contentType);
    }

    public async static Task ApiRoute<I, O>(HttpListenerContext context, Func<I, Task<O>> route)
    {
        await Route(context, async text =>
        {
            var input = JsonConvert.DeserializeObject<I>(text);
            var output = await route(input);
            return JsonConvert.SerializeObject(output);
        }, "application/json");
    }

    public async static Task ApiRoute<O>(HttpListenerContext context, Func<Task<O>> route)
    {
        await Route(context, async text =>
        {
            var output = await route();
            return JsonConvert.SerializeObject(output);
        }, "application/json");
    }

    public static void GetRoute(HttpListenerContext context, Action<Dictionary<string, string>, HttpListenerResponse> route)
    {
        var req = context.Request;
        var dict = req.Url.Query
            .Substring(1)
            .Split('&')
            .Select(param => param.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        route(dict, context.Response);
    }

    public static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        throw new Exception("No network adapters with an IPv4 address in the system!");
    }
}