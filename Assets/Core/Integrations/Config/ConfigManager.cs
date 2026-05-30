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
    public string SharedConfigPath = "hbox.json";
    public string ConfigPath = "config.json";

    private static readonly JsonMergeSettings ConfigMergeSettings = new JsonMergeSettings
    {
        MergeArrayHandling = MergeArrayHandling.Replace,
        MergeNullValueHandling = MergeNullValueHandling.Merge
    };

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
        var j = LoadMergedConfigArray();
        if (j == null)
            return;

        foreach (var i in j)
        {
            var type = i["Type"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(type))
                continue;
            if (!handlers.ContainsKey(type))
                continue;
            var obj = JsonConvert.DeserializeObject(i.ToString(), casters[type]);
            foreach (var handler in handlers[type])
                handler(obj);
            configs.Add(obj);
            configTypes[obj] = type;
        }
    }

    private JArray LoadMergedConfigArray()
    {
        var shared = LoadConfigArray(SharedConfigPath);
        var specific = LoadConfigArray(ConfigPath);

        if (shared == null)
            return specific;
        if (specific == null)
            return shared;

        var mergedConfigs = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        var configOrder = new List<string>();

        MergeConfigArrayInto(shared, mergedConfigs, configOrder);
        MergeConfigArrayInto(specific, mergedConfigs, configOrder);

        var result = new JArray();
        foreach (var type in configOrder)
            result.Add(mergedConfigs[type]);

        return result;
    }

    private static JArray LoadConfigArray(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JArray.Parse(json);
    }

    private static void MergeConfigArrayInto(JArray source, Dictionary<string, JObject> mergedConfigs, List<string> configOrder)
    {
        foreach (var token in source)
        {
            if (!(token is JObject config))
                continue;

            var type = config["Type"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(type))
                continue;

            if (mergedConfigs.TryGetValue(type, out var existingConfig))
            {
                existingConfig.Merge(config, ConfigMergeSettings);
                continue;
            }

            mergedConfigs[type] = (JObject)config.DeepClone();
            configOrder.Add(type);
        }
    }

    public async Task SaveConfigs()
    {
        var json = JsonConvert.SerializeObject(configs);
        await File.WriteAllTextAsync(ConfigPath, json);
    }
}
