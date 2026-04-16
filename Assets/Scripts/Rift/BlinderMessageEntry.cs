using System;
using UnityEngine;

[Serializable]
public struct BlinderMessageEntry
{
    [TextArea] public string line;
    [Min(0f)] public float weight;   // 0 = never picked, 1 = normal, >1 = more common
}
