using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class RuntimeAssetCache
{
    private sealed class CacheEntry
    {
        public UnityEngine.Object Asset;
        public string Category;
        public HashSet<string> Owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object sync = new object();
    private static readonly Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> ownerPaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string[]> assetNameCatalog = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public static string BuildContextOwnerKey(string contextKey)
    {
        return string.IsNullOrWhiteSpace(contextKey) ? "context:unknown" : $"context:{contextKey}";
    }

    public static Texture2D LoadContextTexture(string contextName, string category, string assetName, string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(contextName) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(assetName))
            return null;

        var path = $"{contextName}/{category}/{assetName}";
        return LoadAsset(path, ownerKey, category, Resources.Load<Texture2D>);
    }

    public static string[] GetContextAssetNames<T>(string contextName, string category)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(contextName) || string.IsNullOrWhiteSpace(category))
            return Array.Empty<string>();

        var catalogKey = $"{typeof(T).Name}:{contextName}/{category}";

        lock (sync)
        {
            if (assetNameCatalog.TryGetValue(catalogKey, out var cachedNames) && cachedNames != null)
                return cachedNames;
        }

        var path = $"{contextName}/{category}";
        var loadedAssets = Resources.LoadAll<T>(path) ?? Array.Empty<T>();
        var names = loadedAssets
            .Where(asset => asset != null)
            .Select(asset => asset.name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var asset in loadedAssets)
        {
            if (asset == null)
                continue;

            try
            {
                Resources.UnloadAsset(asset);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"RuntimeAssetCache failed to unload catalog asset '{asset.name}': {e.Message}");
            }
        }

        lock (sync)
        {
            assetNameCatalog[catalogKey] = names;
        }

        return names;
    }

    public static SoundGroup LoadContextSoundGroup(string contextName, string assetName, string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(contextName) || string.IsNullOrWhiteSpace(assetName))
            return null;

        var path = $"{contextName}/SoundGroups/{assetName}";
        return LoadAsset(path, ownerKey, "SoundGroups", Resources.Load<SoundGroup>);
    }

    public static void ReleaseOwner(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
            return;

        List<UnityEngine.Object> assetsToUnload = null;

        lock (sync)
        {
            if (!ownerPaths.TryGetValue(ownerKey, out var paths) || paths == null || paths.Count == 0)
                return;

            assetsToUnload = new List<UnityEngine.Object>();
            foreach (var path in paths.ToList())
            {
                if (!entries.TryGetValue(path, out var entry) || entry == null)
                    continue;

                entry.Owners.Remove(ownerKey);
                if (entry.Owners.Count > 0)
                    continue;

                entries.Remove(path);
                if (entry.Asset != null)
                    assetsToUnload.Add(entry.Asset);
            }

            ownerPaths.Remove(ownerKey);
        }

        foreach (var asset in assetsToUnload)
        {
            try
            {
                Resources.UnloadAsset(asset);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"RuntimeAssetCache failed to unload asset '{asset?.name}': {e.Message}");
            }
        }
    }

    public static RuntimeAssetCacheDiagnostics GetDiagnostics()
    {
        lock (sync)
        {
            var backgrounds = 0;
            var flags = 0;
            var props = 0;
            var soundGroups = 0;

            foreach (var entry in entries.Values)
            {
                if (entry == null)
                    continue;

                if (string.Equals(entry.Category, "Backgrounds", StringComparison.OrdinalIgnoreCase))
                    backgrounds++;
                else if (string.Equals(entry.Category, "Flags", StringComparison.OrdinalIgnoreCase))
                    flags++;
                else if (string.Equals(entry.Category, "Props", StringComparison.OrdinalIgnoreCase))
                    props++;
                else if (string.Equals(entry.Category, "SoundGroups", StringComparison.OrdinalIgnoreCase))
                    soundGroups++;
            }

            return new RuntimeAssetCacheDiagnostics
            {
                ownerCount = ownerPaths.Count,
                totalCachedAssetCount = entries.Count,
                liveBackgroundCount = backgrounds,
                liveFlagCount = flags,
                livePropCount = props,
                liveSoundGroupCount = soundGroups
            };
        }
    }

    private static T LoadAsset<T>(string path, string ownerKey, string category, Func<string, T> loader)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        lock (sync)
        {
            if (entries.TryGetValue(path, out var existing) && existing?.Asset != null)
            {
                AttachOwner(ownerKey, path, existing);
                return existing.Asset as T;
            }
        }

        var asset = loader(path);
        if (asset == null)
            return null;

        lock (sync)
        {
            if (entries.TryGetValue(path, out var existing) && existing?.Asset != null)
            {
                AttachOwner(ownerKey, path, existing);
                return existing.Asset as T;
            }

            var created = new CacheEntry
            {
                Asset = asset,
                Category = category ?? string.Empty
            };
            AttachOwner(ownerKey, path, created);
            entries[path] = created;
            return asset;
        }
    }

    private static void AttachOwner(string ownerKey, string path, CacheEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(path))
            return;

        entry.Owners.Add(ownerKey);
        if (!ownerPaths.TryGetValue(ownerKey, out var paths))
        {
            paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ownerPaths[ownerKey] = paths;
        }

        paths.Add(path);
    }
}

[Serializable]
public sealed class RuntimeAssetCacheDiagnostics
{
    public int ownerCount;
    public int totalCachedAssetCount;
    public int liveBackgroundCount;
    public int liveFlagCount;
    public int livePropCount;
    public int liveSoundGroupCount;
}
