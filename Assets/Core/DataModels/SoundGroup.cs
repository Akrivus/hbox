using UnityEngine;

[CreateAssetMenu(fileName = "SoundGroup", menuName = "UN/SoundGroup")]
public class SoundGroup : ScriptableObject
{
    [Header("Sound Group")]
    public string Name;

    [Header("Sounds")]
    public AudioClip[] Sounds;
}