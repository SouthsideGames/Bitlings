using System;
using UnityEngine;

[Serializable]
public struct TrainingBonus
{
    public int hp;
    public int atk;
    public int def;
    public int spd;

    public void Add(TrainingBonus add)
    {
        hp  += add.hp;
        atk += add.atk;
        def += add.def;
        spd += add.spd;
    }

    public void Clear()
    {
        hp = atk = def = spd = 0;
    }
}
