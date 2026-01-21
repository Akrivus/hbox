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

    private void Start()
    {
        ChatManagerContext.Current.ConfigManager.RegisterConfig(typeof(TTSConfigs), "tts", (_config) => Configure((TTSConfigs)_config));
    }
}
