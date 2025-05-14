using System.Linq;
using UnityEngine;

public class MemoryManager : MonoBehaviour
{
    public void Start()
    {
        ChatManager.Instance.OnChatQueueEmpty += SaveMemories;
    }

    public void OnApplicationQuit()
    {
        SaveMemories();
    }

    private async void SaveMemories()
    {
        var buckets = MemoryBucket.Buckets.Values.ToArray();
        foreach (var bucket in buckets)
            await bucket.Save();
    }
}