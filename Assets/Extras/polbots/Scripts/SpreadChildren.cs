using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpreadChildren : MonoBehaviour
{
    public float xSpread = 1;
    public float ySpread = 1;
    public float zSpread = 1;

    void Start()
    {
        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).localPosition = new Vector3(
                xSpread * i,
                ySpread * i, 
                zSpread * i);
    }
}
