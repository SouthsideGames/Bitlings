using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-only (non-serialized) run state for Iron Career.
/// Sealed mode: never saved; quitting/pause = forfeit.
/// </summary>
[Serializable]
public sealed class IronCareerRunState
{
    public enum IronCareerMode { Standard = 0, Hardcore = 1 }

    [Header("Run")]
    public IronCareerMode mode = IronCareerMode.Standard;
    public int wins = 0;
    public int seed = 0;
    public bool runActive = false;

    [Header("Party")]
    public readonly List<IronMonster> party = new List<IronMonster>(3);
    public int activeIndex = 0;

    [Header("Carry (player-only)")]
    public IronFieldStatusSnapshot carryStatus = IronFieldStatusSnapshot.None;
    public float[] carryShields = new float[3];

    // Cache the last wild encounter rolled so battle + hire offer share the same outcome.
    [NonSerialized] public IronMonster lastRolledWild;

    public void Reset(IronCareerMode newMode, int newSeed)
    {
        mode = newMode;
        seed = newSeed;
        wins = 0;
        runActive = true;
        activeIndex = 0;
        party.Clear();
        carryStatus = IronFieldStatusSnapshot.None;
        carryShields = new float[3];
        lastRolledWild = null;
    }
}
