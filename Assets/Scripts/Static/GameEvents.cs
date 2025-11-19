using System;
using UnityEngine;

public static class GameEvents
{
    public static Action OnTeamChanged;
    public static Action OnResourcesChanged;
    public static Action OnJobsChanged;

    public static Action JobGlobalModsChanged;

    public static Action<string, int> MonsterLeveled;
    public static Action<string> EvolutionOffered;
    public static Action<string> MonsterEvolved;
    public static Action<string, MonsterType> MonsterCaptured;
    public static Action<MonsterType> StarterChosen;

    public static Action<string, MonsterDataSO> BossSpawned;
    public static Action<string> BossDefeated;

    public static Action<string, string, int, int> ShowRewardPopup;

    public static Action<BattleResult> BattleFinished;

    public static Action<ResourceType, int> ResourceAdded;
    public static Action<ResourceType, int> ResourceRemoved;
    public static Action EnergyChanged;

    public static Action<int> IdleBatchCompleted;

    public static Action<int> WinStreakChanged;
}
