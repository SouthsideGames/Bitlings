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
    public static Action<BattleResult> BattleFinished; // add this
}
