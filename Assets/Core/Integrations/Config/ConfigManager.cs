using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class ConfigManager : MonoBehaviour
{
    public string ConfigPath = "config.json";

    private Dictionary<string, Type> casters = new Dictionary<string, Type>();
    private Dictionary<string, List<Action<object>>> handlers = new Dictionary<string, List<Action<object>>>();
    private List<object> configs = new List<object>();
    private Dictionary<object, string> configTypes = new Dictionary<object, string>();

    private void Start()
    {
        StartCoroutine(LateStart());
    }

    private IEnumerator LateStart()
    {
        yield return new WaitForEndOfFrame();
        LoadConfigs();
    }

    public void RegisterConfig(Type cast, string type, Action<object> handler)
    {
        casters[type] = cast;

        if (!handlers.TryGetValue(type, out var handlerList))
        {
            handlerList = new List<Action<object>>();
            handlers[type] = handlerList;
        }

        if (!handlerList.Contains(handler))
            handlerList.Add(handler);

        foreach (var config in configs)
        {
            if (config == null)
                continue;
            if (!configTypes.TryGetValue(config, out var configType))
                continue;
            if (!string.Equals(configType, type, StringComparison.OrdinalIgnoreCase))
                continue;

            handler(config);
        }
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
            var obj = JsonConvert.DeserializeObject(i.ToString(), casters[type]);
            foreach (var handler in handlers[type])
                handler(obj);
            configs.Add(obj);
            configTypes[obj] = type;
        }
    }

    public async Task SaveConfigs()
    {
        var json = JsonConvert.SerializeObject(configs);
        await File.WriteAllTextAsync(ConfigPath, json);
    }
}
