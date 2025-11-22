using UnityEngine;
using System;
using System.Collections;
using System.Reflection;
using Random = UnityEngine.Random;

public partial class EncounterManager : MonoBehaviour
{
    public static EncounterManager I { get; private set; }

    [Obsolete("UI no longer renders inline status. Use BattleLogger instead.")]
    public event Action<string> OnStatus;
    public event Action OnStateChanged;
    public static event Action<int, int> OnEnergyGained;

    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;

    [Header("Boss Settings")]
    [Tooltip("0 = use PlayerData.bossEveryN")]
    [SerializeField, Min(0)] private int bossEveryNOverride = 0;
    [Tooltip("Flat level bonus applied to boss encounters")]
    [SerializeField, Min(0)] private int bossLevelBonus = 2;

    [Header("Options")]
    [SerializeField] private float postResultDelay = 0.8f;
    [SerializeField] private float autoPollSeconds = 0.25f;

    // Runtime state
    private bool _currentEncounterIsBoss = false;
    private MonsterDataSO _currentBossUsed = null;

    private bool inBattle;
    private bool autoMode;
    private bool nextEncounterFree;
    private bool autoRunPaidEnergy;

    private Coroutine postResultCo;
    private Coroutine autoLoopCo;

    private int _currentWinStreak = 0;
    public int CurrentWinStreak => _currentWinStreak;

    // ─────────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        // Win streak & energy
        _currentWinStreak = LoadWinStreakOr(0);
        _currentWinStreak = Mathf.Max(0, _currentWinStreak);

        LoadEnergy();
        ApplyOfflineRegen();
        MirrorEnergyIntoSaveData();

        // Make sure save is ready and shiny synergy is up to date
        SaveManager.LoadOrCreate();
        SaveManager.Data.EnsureTransientSets();
        GlobalEffects.RecalcShinySynergy();

        inBattle = false;
        autoMode = false;
        nextEncounterFree = false;
        autoRunPaidEnergy = false;

        ResourceBank.EnsureSize();

        // Manual by default → summaries can pop immediately
        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("Tap ENCOUNTER to begin. Hold to toggle AUTO.", LogScope.System);
        OnStateChanged?.Invoke();

        NormalizeTeamHPIfUninitialized();
        GameEvents.WinStreakChanged?.Invoke(_currentWinStreak);

        // initial UI update with correct energy mirrored
        MirrorEnergyIntoSaveData();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    void OnDisable()
    {
        if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }
        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        StopAllCoroutines();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }
        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        StopAllCoroutines();
    }

    void Start()
    {
        // Already ensured in Awake, but Start is a safe place for UI messaging
        EmitStatus("Tap ENCOUNTER to begin. Hold to toggle AUTO.", LogScope.System);
        OnStateChanged?.Invoke();
    }

    void Update()
    {
        TickEnergyRuntime();
    }

    // ============================ PUBLIC API (UI) ===============================

    public void RequestEncounterTap()
    {
        if (inBattle) return;

        if (!autoMode && nextEncounterFree)
        {
            nextEncounterFree = false;
            OnStateChanged?.Invoke();
            StartEncounter(spendEnergy: false);
            return;
        }

        if (!HasEnergy())
        {
            EmitStatus("Out of energy!", LogScope.System);
            return;
        }

        StartEncounter(spendEnergy: true);
    }

    public void ToggleAutoMode()
    {
        autoMode = !autoMode;

        if (autoMode) IdleBattleManager.I?.EnableAuto();
        else          IdleBattleManager.I?.DisableAuto();

        if (autoMode)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;

            if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }
            autoLoopCo = StartCoroutine(AutoLoop());

            PostBattleSummaryManager.I?.SetAutoBattling(true);

            if (!inBattle)
                EmitStatus("AUTO mode ON. Battling until defeat…", LogScope.System);
            else
                EmitStatus("AUTO mode ON. Will continue after this battle…", LogScope.System);
        }
        else
        {
            autoRunPaidEnergy = false;

            if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }

            PostBattleSummaryManager.I?.SetAutoBattling(false);

            EmitStatus("AUTO mode OFF. Tap ENCOUNTER for the next fight.", LogScope.System);
        }

        OnStateChanged?.Invoke();
    }

    // ============================= ENCOUNTER FLOW ===============================

    void StartEncounter(bool spendEnergy)
    {
        var data = SaveManager.Data;

        if (data == null || data.team == null || data.team.Count == 0)
        {
            EmitStatus("No team yet. Catch something to begin!", LogScope.System);
            StopAuto_NoEnergy();
            return;
        }

        if (!HasHealthyMonsters())
        {
            EmitStatus("All team members are down. Heal up first.", LogScope.System);
            StopAuto_NoEnergy();
            return;
        }

        if (spendEnergy)
        {
            if (!SpendEnergy())
            {
                StopAuto_NoEnergy();
                EmitStatus("Out of energy!", LogScope.System);
                return;
            }
        }

        int cadence = (bossEveryNOverride > 0)
            ? bossEveryNOverride
            : (data != null && data.bossEveryN > 0 ? data.bossEveryN : 10);

        _currentEncounterIsBoss = ShouldSpawnBoss(
            data != null ? data.encountersSinceBoss : 0,
            cadence
        );
        _currentBossUsed = null;

        MonsterDataSO wild = null;

        if (_currentEncounterIsBoss)
        {
            var lib = MonsterLibraryLocator.Lib;
            _currentBossUsed = PickBossWeighted(lib, data != null ? data.lastBossId : null);

            if (_currentBossUsed != null)
                wild = _currentBossUsed;
            else
                _currentEncounterIsBoss = false;
        }

        if (wild == null)
            wild = PickWildConsideringLures();

        if (wild == null)
        {
            EmitStatus("No monsters available.", LogScope.System);
            return;
        }

        // Calculate average team level
        int avgTeamLvl = 1;
        if (data.team != null && data.team.Count > 0)
        {
            int sum = 0;
            for (int i = 0; i < data.team.Count; i++)
                sum += data.team[i].level;
            avgTeamLvl = Mathf.Max(1, Mathf.RoundToInt((float)sum / data.team.Count));
        }

        int wildLevel = Mathf.Clamp(avgTeamLvl + Random.Range(-1, 2), 1, 99);
        if (_currentEncounterIsBoss)
            wildLevel = Mathf.Max(1, wildLevel + bossLevelBonus);

        PlayEncounterSfx(wild);

        if (TapBoost.I) TapBoost.I.ResetEncounter();

        var p = data.team[0];
        if (_currentEncounterIsBoss)
            EmitStatus($"⚠️ BOSS ENCOUNTER! {wild.displayName} (Lv {wildLevel}) appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");
        else
            EmitStatus($"Encounter! A wild {wild.displayName} (Lv {wildLevel}) appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");

        BattleLogger.BeginEncounter(_currentEncounterIsBoss
            ? $"BOSS: {wild.displayName} Lv{wildLevel}"
            : $"{wild.displayName} Lv{wildLevel}");

        if (_currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossSpawned?.Invoke(_currentBossUsed.id, _currentBossUsed);

        
        inBattle = true;
        OnStateChanged?.Invoke();

        if (!battleManager)
        {
            EmitStatus("No BattleManager assigned.", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            return;
        }

        battleManager.Begin(wild, wildLevel, OnBattleEnded);
    }

    void OnBattleEnded(BattleResult result)
    {
        // Victory / defeat SFX
        if (AudioManager.I)
        {
            if (result.victory)
                AudioManager.I.PlaySfx(SfxType.Victory);
            else
                AudioManager.I.PlaySfx(SfxType.Defeat);
        }

        // Coin multiplier (placeholder hook)
        int finalCoins = ApplyCoinsGainedMultiplier(result.coinsGained);
        finalCoins = Mathf.Max(0, finalCoins);

        if (finalCoins > 0)
            ResourceManager.I.Add(ResourceType.Coins, finalCoins);

        EmitStatus(result.victory ? $"Victory! +{finalCoins} coins" : "Defeat.");

        // Boss defeat signal
        if (result.victory && _currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossDefeated?.Invoke(_currentBossUsed.id);

        // Boss cadence bookkeeping
        if (SaveManager.Data != null)
        {
            AfterBattleCadenceUpdate(
                ref SaveManager.Data.encountersSinceBoss,
                _currentEncounterIsBoss,
                _currentBossUsed,
                ref SaveManager.Data.lastBossId
            );
        }

        // Capture logic (victory only)
        if (result.victory)
        {
            if (_currentEncounterIsBoss || (result.wildDef != null && result.wildDef.uncatchable))
            {
                EmitStatus(AppendLine(GetLastStatus(), "(This Bitling can’t be captured.)"));
            }
            else
            {
                TryCatch(result.wildDef, result.wildLevel);
            }
        }

        // Win streak update
        if (result.victory) SetWinStreak(_currentWinStreak + 1);
        else                SetWinStreak(0);

        ReconcileHPWithCurrentWinStreak();

        OnStateChanged?.Invoke();

        // Persist non-resource state changes
        SaveManager.Save();

        // Broadcast finished event with real coins credited
        var finished = result;
        finished.coinsGained = finalCoins;
        GameEvents.BattleFinished?.Invoke(finished);

        BattleLogger.EndEncounter(result.victory);

        // Continue post-result flow
        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        postResultCo = StartCoroutine(PostResultFlow(result.victory));
    }

    private int ApplyCoinsGainedMultiplier(int baseCoins)
    {
        if (baseCoins <= 0) return 0;
        const float MULT = 1f; // hook for future titles/meta
        return Mathf.Max(0, Mathf.FloorToInt(baseCoins * MULT));
    }

    IEnumerator PostResultFlow(bool victory)
    {
        yield return new WaitForSeconds(postResultDelay);
        inBattle = false;

        if (!victory)
        {
            // Defeat
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                EmitStatus("Defeat. Retrying (AUTO)…", LogScope.System);
                // summary stays queued
                yield break;
            }

            // Manual: show summary immediately
            EmitStatus("Battle finished. Showing summary…", LogScope.System);
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();
            yield break;
        }

        // Victory
        if (autoMode)
        {
            // AUTO: chain battles; no summary pop yet
            if (!autoRunPaidEnergy)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                autoRunPaidEnergy = true;
            }
            StartEncounter(false);
            yield break;
        }

        // Manual victory: next is free + show summary
        nextEncounterFree = true;
        OnStateChanged?.Invoke();
        EmitStatus("Battle finished. Showing summary…", LogScope.System);

        PostBattleSummaryManager.I?.SetAutoBattling(false);
        PostBattleSummaryManager.I?.FlushNowIfPossible();
    }

    // ================= Idle helpers / State getters ============================

    public long GetLastSavedUnix() => SaveManager.Data.lastSavedUnix;

    public bool IsAutoModeAllowedInBackground()
    {
        if (inBattle) return false;
        return true;
    }

    public bool IsInBattle      => inBattle;
    public bool IsAutoMode      => autoMode;
    public bool NextEncounterIsFree => nextEncounterFree;

    void EmitStatus(string msg, LogScope scope = LogScope.Encounter)
    {
        if (!string.IsNullOrEmpty(msg))
            BattleLogger.Log(msg, scope);
        OnStatus?.Invoke(msg);
    }

    void NormalizeTeamHPIfUninitialized()
    {
        var lib  = MonsterLibraryLocator.Lib;
        var team = SaveManager.Data?.team;
        if (!lib || team == null) return;

        bool changed = false;
        for (int i = 0; i < team.Count; i++)
        {
            var om = team[i];
            if (om == null || string.IsNullOrEmpty(om.monsterId)) continue;

            if (om.currentHP >= 0) continue; // only when -1 (uninitialized)

            var def = lib.GetById(om.monsterId);
            if (!def) continue;

            int maxHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, Mathf.Max(1, om.level)));
            om.currentHP = Mathf.Max(1, maxHP);
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
            if (m.currentHP != 0) return true;
        }
        return false;
    }

    string GetLastStatus() => null; // kept for AppendLine compatibility

    string AppendLine(string a, string b)
        => string.IsNullOrEmpty(a) ? b : (a + "\n" + b);

    // ───────────────────────── Encounter SFX helpers ─────────────────────────
    private void PlayEncounterSfx(MonsterDataSO wild)
    {
        if (AudioManager.I == null || wild == null)
            return;

        // Boss has highest priority
        if (_currentEncounterIsBoss)
        {
            AudioManager.I.PlaySfx(SfxType.BossEncounter);
            return;
        }

        // Shiny encounter
        if (IsShinyMonster(wild))
        {
            AudioManager.I.PlaySfx(SfxType.ShinyEncounter);
            return;
        }

        // Unique / special encounter (Legendary/Mythic)
        if (IsUniqueMonster(wild))
        {
            AudioManager.I.PlaySfx(SfxType.UnqiueEncounter); // enum spelling kept
            return;
        }
    }

    // ========================================================================
    // WIN STREAK SYSTEM (self-contained in this file)
    // ========================================================================

    /// <summary>
    /// If you later want win streak to buff heal HP or similar, put that logic here.
    /// For now it's a no-op so the call compiles safely.
    /// </summary>
    private void ReconcileHPWithCurrentWinStreak()
    {
        // currently no special HP behavior tied to win streak
        // you can hook in bonuses/regen here if desired
    }

    /// <summary>
    /// Reads saved win streak or returns the fallback.
    /// </summary>
    private int LoadWinStreakOr(int fallback)
    {
        try
        {
            var data = SaveManager.Data;
            if (data == null) return fallback;

            return Mathf.Max(0, data.winStreak);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Sets the win streak, fires events, updates SaveManager, and handles clamps.
    /// </summary>
    public void SetWinStreak(int value)
    {
        int clamped = Mathf.Max(0, value);

        if (_currentWinStreak == clamped)
            return;

        _currentWinStreak = clamped;

        // Persist to save
        try
        {
            var data = SaveManager.Data;
            if (data != null)
            {
                data.winStreak = clamped;
            }
        }
        catch
        {
        }

        // Broadcast streak update (UI, titles, battle manager, etc.)
        try
        {
            GameEvents.WinStreakChanged?.Invoke(clamped);
        }
        catch { }

        BattleLogger.Log($"Win streak: {_currentWinStreak}", LogScope.System);
    }

    public int GetWinStreak() => _currentWinStreak;
}
