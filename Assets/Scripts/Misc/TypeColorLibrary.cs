using UnityEngine;
using System.Collections.Generic;

public static class TypeColorLibrary
{
    private static readonly Dictionary<MonsterType, Color> Colors = new()
    {
        { MonsterType.Fire,     new Color32(230, 74,  25, 255) },
        { MonsterType.Water,    new Color32( 30, 136, 229, 255) },
        { MonsterType.Grass,    new Color32( 56, 142,  60, 255) },
        { MonsterType.Electric, new Color32(255, 193,   7, 255) },
        { MonsterType.Ice,      new Color32( 79, 195, 247, 255) },
        { MonsterType.Clash,    new Color32(121,  85,  72, 255) },
        { MonsterType.Corrupt,  new Color32(156,  39, 176, 255) },
        { MonsterType.Ground,   new Color32(141, 110,  99, 255) },
        { MonsterType.Sky,      new Color32( 63,  81, 181, 255) },
        { MonsterType.Oracle,   new Color32(  0, 150, 136, 255) },
        { MonsterType.Bug,      new Color32(104, 159,  56, 255) },
        { MonsterType.Rock,     new Color32(120, 144, 156, 255) },
        { MonsterType.Specter,  new Color32(103,  58, 183, 255) },
        { MonsterType.Wyrm,     new Color32(255, 112,  67, 255) },
        { MonsterType.Umbral,   new Color32( 97,  97,  97, 255) },
        { MonsterType.Alloy,    new Color32(158, 158, 158, 255) },
    };

    public static Color Get(MonsterType type)
    {
        return Colors.TryGetValue(type, out var c) ? c : Color.white;
    }
}
