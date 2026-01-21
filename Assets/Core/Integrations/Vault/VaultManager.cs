using System.Collections;
using System.Diagnostics;
using UnityEngine;

public class VaultManager : MonoBehaviour
{
    public static VaultManager Instance => _instance ??= FindFirstObjectByType<VaultManager>();
    private static VaultManager _instance;

    private void Awake()
    {
        StartCoroutine(GitPullVaultCoroutine());
    }

    private IEnumerator GitPullVaultCoroutine()
    {
        do
        {
           yield return new WaitForSeconds(300f);
            RunGitPullVault();
        } while (Application.isPlaying);
    }

    static void RunGitPullVault()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "-C \"./Vault\" pull --no-ff --no-edit",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }).WaitForExit();
    }
}