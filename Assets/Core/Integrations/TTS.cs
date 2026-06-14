using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class TTS : MonoBehaviour, IConfigurable<TTSConfigs>
{
    public static string GoogleApiKey;
    public static string OpenAiApiKey;
    public static int MaxConcurrentRequests = 1;

    private static readonly object requestGateSync = new object();
    private static SemaphoreSlim requestGate = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);

    public AudioSource source;

    public void Configure(TTSConfigs config)
    {
        GoogleApiKey = config.GoogleApiKey;
        OpenAiApiKey = config.OpenAiApiKey;
        ConfigureConcurrency(config.MaxConcurrentRequests);
    }

    public static async Task WaitForRequestSlot()
    {
        await requestGate.WaitAsync();
    }

    public static void ReleaseRequestSlot()
    {
        requestGate.Release();
    }

    private static void ConfigureConcurrency(int maxConcurrentRequests)
    {
        var normalized = Mathf.Max(1, maxConcurrentRequests);
        lock (requestGateSync)
        {
            if (normalized == MaxConcurrentRequests)
                return;

            MaxConcurrentRequests = normalized;
            requestGate = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);
        }
    }

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(TTSConfigs), "tts", (_config) => Configure((TTSConfigs)_config));
    }
}
