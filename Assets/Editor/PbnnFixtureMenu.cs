using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PbnnFixtureMenu
{
    private const string PromptSource = "Docs/PBNN/Prompts";
    private const string PromptDestination = "Vault/pbnn/Prompts";
    private const string FixturePath = "Docs/PBNN/FixtureBulletin.txt";

    [MenuItem("HBOx/PBNN/Install Fixture Prompts")]
    public static void InstallPrompts()
    {
        if (!Directory.Exists(PromptSource))
        {
            Debug.LogError($"PBNN prompt source is missing: {PromptSource}");
            return;
        }

        foreach (var source in Directory.GetFiles(PromptSource, "*.md", SearchOption.AllDirectories))
        {
            var relative = source.Substring(PromptSource.Length).TrimStart('/', '\\');
            var destination = Path.Combine(PromptDestination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
        }
        Debug.Log("Installed PBNN fixture prompts in Vault/pbnn/Prompts.");
    }

    [MenuItem("HBOx/PBNN/Queue Fixture Bulletin")]
    public static void QueueFixture()
    {
        if (!Application.isPlaying || ChatManagerContext.Current?.Key != "pbnn")
        {
            Debug.LogError("Start Main in Play mode and select PBNN before queuing its fixture.");
            return;
        }

        var generator = ChatManagerContext.Current.GetComponentsInChildren<ChatGenerator>()
            .FirstOrDefault(item => item.name == "Default");
        if (generator == null || !File.Exists(FixturePath))
        {
            Debug.LogError("PBNN needs its Default generator and Docs/PBNN/FixtureBulletin.txt.");
            return;
        }

        if (generator.IsGenerating || generator.QueueDepth > 0)
        {
            Debug.LogWarning("PBNN already has a generation in progress or queued.");
            return;
        }

        if (!File.Exists(Path.Combine(PromptDestination, "Defaults.md")))
        {
            Debug.LogError("Run HBOx/PBNN/Install Fixture Prompts before queuing the fixture.");
            return;
        }

        generator.AddPromptToQueue(File.ReadAllText(FixturePath), GenerationDisposition.SaveAndPlay);
        Debug.Log("Queued the historical PBNN sports fixture through the normal generation/save/playback pipeline.");
    }
}
