using System;
using UnityEngine;

public static class GameEvents
{
    // ─────────────────────────────────────────────────────────────
    // Core state changes
    // ─────────────────────────────────────────────────────────────
    public static Action OnTeamChanged;
    public static Action OnResourcesChanged;
    public static Action OnJobsChanged;

    // World job debuffs (e.g., boss auras, global modifiers)
    public static Action JobGlobalModsChanged;

    // ─────────────────────────────────────────────────────────────
    // Monster lifecycle / progression
    // ─────────────────────────────────────────────────────────────
    /// <summary>Owned monster leveled: (ownedIdOrDefId, newLevel)</summary>
    public static Action<string, int> MonsterLeveled;

    /// <summary>Evolution prompt was offered to this owned monster.</summary>
    public static Action<string> EvolutionOffered;

    /// <summary>Owned monster completed evolution.</summary>
    public static Action<string> MonsterEvolved;

    /// <summary>A wild monster was captured. Args: ownedIdOrDefId, captured type.</summary>
    public static Action<string, MonsterType> MonsterCaptured;

    /// <summary>Player selected their starter. Arg: selected type.</summary>
    public static Action<MonsterType> StarterChosen;

    // ─────────────────────────────────────────────────────────────
    // Boss lifecycle
    // ─────────────────────────────────────────────────────────────
    public static Action<string, MonsterDataSO> BossSpawned;
    public static Action<string> BossDefeated;

    // ─────────────────────────────────────────────────────────────
    // UI helpers
    // ─────────────────────────────────────────────────────────────
    /// <summary>Show a reward popup. Args: title, iconKey, amount, rarityTier.</summary>
    public static Action<string, string, int, int> ShowRewardPopup;

    // ─────────────────────────────────────────────────────────────
    // Battle
    // ─────────────────────────────────────────────────────────────
    /// <summary>Broadcast a completed battle result.</summary>
    public static Action<BattleResult> BattleFinished;

    // ─────────────────────────────────────────────────────────────
    // Resources
    // ─────────────────────────────────────────────────────────────
    public static Action<ResourceType, int> ResourceAdded;
    public static Action<ResourceType, int> ResourceRemoved;
    public static Action EnergyChanged;

    // ─────────────────────────────────────────────────────────────
    // Idle loop
    // ─────────────────────────────────────────────────────────────
    public static Action<int> IdleBatchCompleted;

    /// <summary>Win streak in encounters changed.</summary>
    public static Action<int> WinStreakChanged;
}
