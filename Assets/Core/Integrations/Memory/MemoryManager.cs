using System.Linq;
using UnityEngine;

public class MemoryManager : MonoBehaviour
{
    private ChatManagerContext boundContext;

    public void Start()
    {
        boundContext = ChatManagerContext.Current;
        if (boundContext == null)
            return;

        boundContext.OnChatQueueEmpty += SaveMemories;
    }

    public void OnDestroy()
    {
        if (boundContext != null)
            boundContext.OnChatQueueEmpty -= SaveMemories;

        SaveMemories();
    }

    private async void SaveMemories()
    {
        var buckets = MemoryBucket.Buckets.Values.ToArray();
        foreach (var bucket in buckets)
            await bucket.Save();
    }
}
