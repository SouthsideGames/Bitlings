using System;
using UnityEngine;

public static class GameEvents
{
    // ─────────────────────────────────────────────────────────
    // Global / Common
    // ─────────────────────────────────────────────────────────
    public static Action OnTeamChanged;
    public static Action OnTeamHealthChanged;
    public static Action OnResourcesChanged;
    public static Action OnJobsChanged;
    public static Action JobGlobalModsChanged;
    public static Action OnSaveReloaded;
    public static Action OnSettingsApplied;
    public static Action<bool> HardResetting;
    public static Action<string> ToastRequested;

    // ─────────────────────────────────────────────────────────
    // Monsters / Progression
    // ─────────────────────────────────────────────────────────
    public static Action<string, int> MonsterLeveled;
    public static Action<string> EvolutionOffered;
    public static Action<string> MonsterEvolved;
    public static Action<string, string, string> EvolutionCeremonyRequested; // (oldId, newId, ownedUID)
    public static Action<string, MonsterType> MonsterCaptured;
    public static Action<MonsterType> StarterChosen;
    public static Action OnOwnedMonstersChanged;

    // ─────────────────────────────────────────────────────────
    // Boss / Rifts / Battle
    // ─────────────────────────────────────────────────────────
    public static Action<string, MonsterDataSO> BossSpawned;
    public static Action<string> BossDefeated;
    public static Action<string, string, int, int> ShowRewardPopup;
    public static Action<BattleResult> BattleFinished;
    public static Action OnBattleStateChanged;
    public static Action OnRiftAutoModeChanged;
    public static Action<bool> AutoBattleModeChanged;

    // ─────────────────────────────────────────────────────────
    // Resources / Energy
    // ─────────────────────────────────────────────────────────
    public static Action<ResourceType, int> ResourceAdded;
    public static Action<ResourceType, int> ResourceRemoved;
    public static Action EnergyChanged;

    // ─────────────────────────────────────────────────────────
    // Idle / Meta
    // ─────────────────────────────────────────────────────────
    public static Action<int> IdleBatchCompleted;
    public static Action<int> WinStreakChanged;
    public static Action FavoritesChanged;
    public static Action Tutorial_FirstJobAssigned;
    public static Action OnBoostersChanged;

    // ─────────────────────────────────────────────────────────
    // Achievement-relevant
    // ─────────────────────────────────────────────────────────
    public static Action JobAssigned;
    public static Action TitleEquipped;
    public static Action DirectoryOpened;
    public static Action<StatusType> StatusAppliedToWild;
    public static Action ExecutiveTrialStarted;
    public static Action ExecutiveTrialBattleWon;
    public static Action<int, bool, int> ExecutiveTrialCompleted; // (wins, forfeited, totalDeaths)

    // ─────────────────────────────────────────────────────────
    // Promotions (Ranks)
    // ─────────────────────────────────────────────────────────
    // (rank, xp, xpThisRank, xpToNext)
    public static Action<int, int, int, int> PromotionProgressChanged;
    // (oldRank, newRank)
    public static Action<int, int> PromotionRankChanged;

        // ───────────────────────────────────────────────────────
    // Features / Unlocks
    // ─────────────────────────────────────────────────────────
    public static Action<FeatureId> FeatureUnlocked;

    // ─────────────────────────────────────────────────────────────────────────────
    // Battle stat refresh
    // ─────────────────────────────────────────────────────────────────────────────
    public static Action BattleStatsChanged;

    // ─────────────────────────────────────────────────────────
    // World Events
    // ─────────────────────────────────────────────────────────
    public static Action WorldEventsChanged;

    // ─────────────────────────────────────────────────────────
    // Exchange
    // ─────────────────────────────────────────────────────────
    public static Action<string, int> MonsterBrokered;       // (speciesId, creditPayout)
    public static Action<string, string> RequestFulfilled;   // (requestId, speciesId)
    public static Action ExchangeValuesChanged;
    public static Action ExchangeMarketReset;

    // ─────────────────────────────────────────────────────────
    // Packs / Seasons
    // ─────────────────────────────────────────────────────────
    public static Action PackSeasonChanged;

    // ─────────────────────────────────────────────────────────
    // Arena
    // ─────────────────────────────────────────────────────────
    public static Action ArenaDataChanged;

    // Legends / Honor
    public static Action<string> MentorRetired;
    public static Action<string> HonorApplied;

    public static event Action AutoApplyRequested;
    

    public static void RaiseBattleStatsChanged()
    {
        BattleStatsChanged?.Invoke();
    }

    public static void RaiseToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ToastRequested?.Invoke(message);
    }

    public static void RaiseFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.None) return;
        FeatureUnlocked?.Invoke(feature);
    }

    public static void RaiseAutoBattleModeChanged(bool isAuto)
    {
        AutoBattleModeChanged?.Invoke(isAuto);

        OnRiftAutoModeChanged?.Invoke();
    }

    public static void RaiseAutoApplyRequested()
    {
        AutoApplyRequested?.Invoke();
    }
}
