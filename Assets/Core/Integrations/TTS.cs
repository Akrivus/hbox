using UnityEngine;

public class TTS : MonoBehaviour, IConfigurable<TTSConfigs>
{
    public static string GoogleApiKey;   
    public static string OpenAiApiKey;

    public AudioSource source;

    public void Configure(TTSConfigs config)
    {
        GoogleApiKey = config.GoogleApiKey;
        OpenAiApiKey = config.OpenAiApiKey;
    }

    private void Awake()
    {
        ConfigManager.Instance.RegisterConfig(typeof(TTSConfigs), "tts", (config) => Configure((TTSConfigs) config));
    }
}
