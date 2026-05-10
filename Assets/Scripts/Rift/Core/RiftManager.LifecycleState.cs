using UnityEngine;
using System.Collections;

// ─────────────────────────────────────────────────────────────
// RiftManager.LifecycleState
// Unity lifecycle hooks, runtime cleanup/reset, shared state helpers.
// ─────────────────────────────────────────────────────────────

public partial class RiftManager
{
    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        // Reduce scene-order brittleness: if BattleManager isn't wired in the inspector,
        // try to locate it so rifts can still proceed.
        if (battleManager == null)
        {
            if (battleManager == null)
                battleManager = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);

            if (battleManager == null)
                Debug.LogWarning("[RiftManager] BattleManager reference is missing. Rifts cannot start battles until a BattleManager exists.");
        }

        _currentWinStreak = LoadWinStreakOr(0);
        _currentWinStreak = Mathf.Max(0, _currentWinStreak);

        LoadEnergy();
        ApplyOfflineRegen();

        SaveManager.LoadOrCreate();
        SaveManager.Data.EnsureTransientSets();
        GlobalEffects.RecalcPremiumSynergy();

        ResetRiftRuntimeFlags();

        ResourceBank.EnsureSize();

        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("Tap RIFT to begin. Hold to toggle AUTO.", LogScope.System);
        OnStateChanged?.Invoke();

        NormalizeTeamHPIfUninitialized();
        GameEvents.WinStreakChanged?.Invoke(_currentWinStreak);

        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    void OnDisable()
    {
        CleanupRiftRuntime();
    }

    void OnDestroy()
    {
        CleanupRiftRuntime();
        if (I == this) I = null;
    }

    void Start()
    {
    }

    void Update()
    {
        TickEnergyRuntime();
    }

    private void ResetRiftRuntimeFlags()
    {
        inBattle = false;
        autoMode = false;
        nextRiftFree = false;
        autoRunPaidEnergy = false;
        _manualHirePending = false;
    }

    private void StopAndClearCoroutine(ref Coroutine coroutine)
    {
        if (coroutine == null) return;
        StopCoroutine(coroutine);
        coroutine = null;
    }

    // Hard stop used when Executive Trial takes over: cancel any pending rift flows
    // without broadcasting auto-mode events (IronEventGuard forbids them during Iron).
    public void ForceStopForIron()
    {
        // Kill any running rift coroutines so they cannot resume after Iron exits.
        StopAndClearCoroutine(ref autoLoopCo);
        StopAndClearCoroutine(ref postResultCo);
        StopAllCoroutines();

        // Reset runtime flags quietly (no GameEvents) to avoid event guard violations.
        inBattle = false;
        autoMode = false;
        nextRiftFree = false;
        autoRunPaidEnergy = false;
        _manualHirePending = false;
    }

    private void CleanupRiftRuntime()
    {
        ClearWildTitleInjection();
        StopAndClearCoroutine(ref autoLoopCo);
        StopAndClearCoroutine(ref postResultCo);
        StopAllCoroutines();
    }

    public long GetLastSavedUnix() => SaveManager.Data.lastSavedUnix;

    public bool IsAutoModeAllowedInBackground()
    {
        if (inBattle) return false;
        return true;
    }

    public bool IsInBattle => inBattle;
    public bool IsAutoMode => autoMode;
    public bool NextRiftIsFree => nextRiftFree;

    void EmitStatus(string msg, LogScope scope = LogScope.Rift)
    {
        if (!string.IsNullOrEmpty(msg))
            BattleLogger.Log(msg, scope);
        OnStatus?.Invoke(msg);
    }

    void NormalizeTeamHPIfUninitialized()
    {
        var lib = MonsterLibraryLocator.Lib;
        var team = SaveManager.Data?.team;
        if (!lib || team == null) return;

        bool changed = false;
        for (int i = 0; i < team.Count; i++)
        {
            var om = team[i];
            if (om == null || string.IsNullOrEmpty(om.monsterId)) continue;

            if (om.currentHP >= 0) continue;

            var def = lib.GetById(om.monsterId);
            if (!def) continue;

            long now = SaveManager.NowUnix();
            OwnedMonsterHP.SetFull(ref om, now, OwnedMonsterHP.Reason.LoadNormalize);
            team[i] = om;
            changed = true;
        }
        if (changed) SaveManager.Save();
    }

    public void RequestStateRefresh()
    {
        OnStateChanged?.Invoke();
    }

    bool HasHealthyMonsters()
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return false;

        for (int i = 0; i < team.Count && i < 3; i++)
        {
            var m = team[i];
            if (m == null) continue;
            if (string.IsNullOrEmpty(m.monsterId)) continue;

            // Healthy means HP > 0.
            // HP < 0 is "uninitialized" (treated as full by InitializeUninitializedTeamHp), so count it as healthy.
            if (m.currentHP > 0 || m.currentHP < 0) return true;
        }
        return false;
    }

    string GetLastStatus() => null;

    string AppendLine(string a, string b)
        => string.IsNullOrEmpty(a) ? b : (a + "\n" + b);
}
