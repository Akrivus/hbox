using UnityEngine;

public class LocationDefinition : MonoBehaviour
{
    public Transform[] SpawnPoints;

    public string Line => GetLine();

    public string GetLine()
    {
        var str = name;
        foreach (var s in SpawnPoints)
            str += $"\n- {s.name}";
        return str;
    }
}