using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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