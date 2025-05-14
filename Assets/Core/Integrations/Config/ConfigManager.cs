using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class ConfigManager : MonoBehaviour
{
    public static ConfigManager Instance => _instance ??= FindFirstObjectByType<ConfigManager>();
    private static ConfigManager _instance;

    public string ConfigPath = "config.json";

    private Dictionary<string, Type> casters = new Dictionary<string, Type>();
    private Dictionary<string, Action<object>> handlers = new Dictionary<string, Action<object>>();
    private List<object> configs = new List<object>();

    private void Awake()
    {
        _instance = this;
    }

    private void Start()
    {
        LoadConfigs();
    }

    public void RegisterConfig(Type cast, string type, Action<object> handler)
    {
        casters[type] = cast;
        handlers[type] = handler;
    }

    public void LoadConfigs()
    {
        if (!File.Exists(ConfigPath))
            return;

        var json = File.ReadAllText(ConfigPath);
        var j = JArray.Parse(json);

        foreach (var i in j)
        {
            var type = i["Type"].Value<string>();
            if (!handlers.ContainsKey(type))
                continue;
            var handler = handlers[type];
            var obj = JsonConvert.DeserializeObject(i.ToString(), casters[type]);
            handler(obj);
            configs.Add(obj);
        }
    }

    public async Task SaveConfigs()
    {
        var json = JsonConvert.SerializeObject(configs);
        await File.WriteAllTextAsync(ConfigPath, json);
    }
}