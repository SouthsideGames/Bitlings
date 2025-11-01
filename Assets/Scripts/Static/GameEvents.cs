using System;
using UnityEngine;

public static class GameEvents
{
    // Core state changes
    public static Action OnTeamChanged;
    public static Action OnResourcesChanged;
    public static Action OnJobsChanged;

    // World job debuffs
    public static Action JobGlobalModsChanged;

    // Monster lifecycle / progression
    public static Action<string, int> MonsterLeveled;
    public static Action<string> EvolutionOffered;
    public static Action<string> MonsterEvolved;
    public static Action<string, MonsterType> MonsterCaptured;
    public static Action<MonsterType> StarterChosen;

    // Boss lifecycle
    public static Action<string, MonsterDataSO> BossSpawned;
    public static Action<string> BossDefeated;

    // UI helpers
    public static Action<string, string, int, int> ShowRewardPopup;

    // NEW: battle result broadcast
    public static Action<BattleResult> BattleFinished;

    // Resources
    public static Action<ResourceType, int> ResourceAdded;    // fires whenever ResourceManager adds
    public static Action<ResourceType, int> ResourceRemoved;  // optional if you’ll ever spend/remove
    public static Action EnergyChanged;                       // used by encounter/idle energy UI

    // Idle loop
    public static Action<int> IdleBatchCompleted;

    /// <summary>Fired after the active monster changes. Args: oldIndex, newIndex.</summary>
    public static event Action<int, int> ActiveMonsterSwapped;

    public static void RaiseActiveMonsterSwapped(int oldIndex, int newIndex)
        => ActiveMonsterSwapped?.Invoke(oldIndex, newIndex);
        
    public static System.Action<int> WinStreakChanged;
    public static void RaiseWinStreakChanged(int value) => WinStreakChanged?.Invoke(value);  
}
