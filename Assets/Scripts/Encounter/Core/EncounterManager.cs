using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Battle Spawn Points")]
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform enemySpawnPoint;

    public Transform PlayerSpawnPoint => playerSpawnPoint;
    public Transform EnemySpawnPoint => enemySpawnPoint;

    // Runtime state
    private bool _currentEncounterIsBoss = false;
    private MonsterDataSO _currentBossUsed = null;

    // Cache the most recent battle result (manual hire decision needs this)
    private BattleResult _lastBattleResult;

    private bool inBattle;
    private bool autoMode;
    private bool nextEncounterFree;
    private bool autoRunPaidEnergy;

    private Coroutine postResultCo;
    private Coroutine autoLoopCo;

    private int _currentWinStreak = 0;
    public int CurrentWinStreak => _currentWinStreak;

    // Tracks whether we are waiting on manual hire decision
    private bool _manualHirePending = false;

    // ─────────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        _currentWinStreak = LoadWinStreakOr(0);
        _currentWinStreak = Mathf.Max(0, _currentWinStreak);

        LoadEnergy();
        ApplyOfflineRegen();

        SaveManager.LoadOrCreate();
        SaveManager.Data.EnsureTransientSets();
        GlobalEffects.RecalcShinySynergy();

        inBattle = false;
        autoMode = false;
        nextEncounterFree = false;
        autoRunPaidEnergy = false;

        ResourceBank.EnsureSize();

        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("Tap ENCOUNTER to begin. Hold to toggle AUTO.", LogScope.System);
        OnStateChanged?.Invoke();

        NormalizeTeamHPIfUninitialized();
        GameEvents.WinStreakChanged?.Invoke(_currentWinStreak);
        
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
        else IdleBattleManager.I?.DisableAuto();

        if (autoMode)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;

            if (autoLoopCo != null)
            {
                StopCoroutine(autoLoopCo);
                autoLoopCo = null;
            }

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

            if (autoLoopCo != null)
            {
                StopCoroutine(autoLoopCo);
                autoLoopCo = null;
            }

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
            wild = PickWildConsideringFlyers();

        if (wild == null)
        {
            EmitStatus("No monsters available.", LogScope.System);
            return;
        }

        FieldOpsTracker.RecordEncounter(wild);

        if (EncounterPanelUI.I)
            EncounterPanelUI.I.OnWildSpawned(wild);

        NotifyAuto_SpecialSpawn(wild);

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

        PostBattleSummaryManager.I?.NotifyBattleStart();

        _manualHirePending = false;
        battleManager.Begin(wild, wildLevel, OnBattleEnded);
    }

    void OnBattleEnded(BattleResult result)
    {
        _lastBattleResult = result;

        bool escaped = result.escaped;
        bool victory = result.victory;
        bool defeat = !victory && !escaped;

        if (AudioManager.I)
        {
            if (victory) AudioManager.I.PlaySfx(SfxType.Victory);
            else if (defeat) AudioManager.I.PlaySfx(SfxType.Defeat);
        }

        int finalcredits = 0;
        if (!escaped)
        {
            finalcredits = ApplycreditsGainedMultiplier(result.creditsGained);
            finalcredits = Mathf.Max(0, finalcredits);

            if (finalcredits > 0)
                ResourceManager.I.Add(ResourceType.Credits, finalcredits);
        }

        if (victory) EmitStatus($"Victory! +{finalcredits} credits");
        else if (defeat) EmitStatus("Defeat.");
        else if (escaped) EmitStatus("The wild Bitling fled.");

        if (victory && _currentEncounterIsBoss && _currentBossUsed != null)
        {
            GameEvents.BossDefeated?.Invoke(_currentBossUsed.id);
            FieldOpsTracker.RecordRiftStabilization(_currentBossUsed);
        }

        if (SaveManager.Data != null)
        {
            AfterBattleCadenceUpdate(
                ref SaveManager.Data.encountersSinceBoss,
                _currentEncounterIsBoss,
                _currentBossUsed,
                ref SaveManager.Data.lastBossId
            );
        }

        // AUTO capture only; manual capture is driven by hire overlay
        if (victory && autoMode)
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

        if (victory) SetWinStreak(_currentWinStreak + 1);
        else if (defeat) SetWinStreak(0);

        ReconcileHPWithCurrentWinStreak();
        OnStateChanged?.Invoke();

        SaveManager.Save();

        var finished = result;
        finished.creditsGained = finalcredits;

        GameEvents.BattleFinished?.Invoke(finished);
        BattleLogger.EndEncounter(victory);

        // ─────────────────────────────────────────────────────────────────────
        // FIX: HOLD the summary BEFORE queuing it on manual-victory hire flow.
        // This prevents NotifyBattleEnd → TryShowNext from opening the summary
        // immediately, which was causing the “summary opens again on hire click”.
        // ─────────────────────────────────────────────────────────────────────
        bool holdForHireDecision =
            victory &&
            !escaped &&
            !autoMode &&
            !_currentEncounterIsBoss &&
            finished.wildDef != null &&
            !finished.wildDef.uncatchable &&
            EncounterPanelUI.I != null;

        _manualHirePending = holdForHireDecision;

        // If we must show Hire first, force the summary manager into HOLD mode now.
        // (NotifyBattleEnd will enqueue, but TryShowNext will no-op while held.)
        if (holdForHireDecision)
            PostBattleSummaryManager.I?.SetAutoBattling(true);
        else
            PostBattleSummaryManager.I?.SetAutoBattling(autoMode);

        // Queue the summary data NOW so nothing is lost (even for manual victories).
        PostBattleSummaryManager.I?.NotifyBattleEnd(
            finished,
            isAuto: autoMode,
            growthCoresGained: 0,
            monstersLeveledUp: 0,
            captured: false,
            capturedMonsterId: null,
            capturedLevel: 0,
            levelUpSummaries: null,
            creditsBase: finalcredits,
            creditsTitleBonus: 0,
            growthCoresBase: 0,
            growthCoresTitleBonus: 0,
            growthCoresDetailLines: null
        );

        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        postResultCo = StartCoroutine(PostResultFlow(victory, escaped));
    }

    private int ApplycreditsGainedMultiplier(int basecredits)
    {
        if (basecredits <= 0) return 0;
        const float MULT = 1f;
        return Mathf.Max(0, Mathf.FloorToInt(basecredits * MULT));
    }

    IEnumerator PostResultFlow(bool victory, bool escaped)
    {
        yield return new WaitForSeconds(postResultDelay);
        inBattle = false;

        // Enemy fled
        if (escaped)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }

                EmitStatus("The wild Bitling fled. Starting next encounter (AUTO)…", LogScope.System);
                StartEncounter(false);
            }
            else
            {
                EmitStatus("The wild Bitling fled. Showing summary…", LogScope.System);
                PostBattleSummaryManager.I?.SetAutoBattling(false);
                PostBattleSummaryManager.I?.FlushNowIfPossible();
            }
            yield break;
        }

        // Defeat
        if (!victory)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                EmitStatus("Defeat. Retrying (AUTO)…", LogScope.System);
                yield break;
            }

            EmitStatus("Battle finished. Showing summary…", LogScope.System);
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();
            yield break;
        }

        // Victory
        if (autoMode)
        {
            if (!autoRunPaidEnergy)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                autoRunPaidEnergy = true;
            }
            StartEncounter(false);
            yield break;
        }

        // Manual victory: HIRE WINDOW FIRST, SUMMARY AFTER
        nextEncounterFree = true;
        OnStateChanged?.Invoke();

        bool canAskHire =
            !_currentEncounterIsBoss &&
            _lastBattleResult.wildDef != null &&
            !_lastBattleResult.wildDef.uncatchable &&
            EncounterPanelUI.I != null;

        if (canAskHire)
        {
            EmitStatus("Victory. Hire decision…", LogScope.System);

            // _manualHirePending should already be true from OnBattleEnded (holdForHireDecision).
            // Keep holding summary while the hire overlay is up.
            PostBattleSummaryManager.I?.SetAutoBattling(true);

            EncounterPanelUI.I.ShowHireDecision(_lastBattleResult.wildDef, _lastBattleResult.wildLevel);
            yield break;
        }

        EmitStatus("Battle finished. Showing summary…", LogScope.System);
        PostBattleSummaryManager.I?.SetAutoBattling(false);
        PostBattleSummaryManager.I?.FlushNowIfPossible();
    }

    // ========================================================================
    // Called by EncounterPanelUI when YES/NO is clicked and capture attempt resolved.
    // ========================================================================
    public void OnHireDecisionResolved(bool hiredYes, bool captureSucceeded)
    {
        if (!_manualHirePending)
        {
            // Safety: prevent double-resolve or late callbacks
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();
            return;
        }

        _manualHirePending = false;

        // Patch the most recently queued summary entry with capture info (if any).
        if (hiredYes && captureSucceeded && _lastBattleResult.wildDef != null)
        {
            PostBattleSummaryManager.I?.TryUpdateLatestQueuedCapture(
                true,
                _lastBattleResult.wildDef.id,
                _lastBattleResult.wildLevel
            );
        }
        else
        {
            PostBattleSummaryManager.I?.TryUpdateLatestQueuedCapture(false, null, 0);
        }

        // Release + show
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

    public bool IsInBattle => inBattle;
    public bool IsAutoMode => autoMode;
    public bool NextEncounterIsFree => nextEncounterFree;

    void EmitStatus(string msg, LogScope scope = LogScope.Encounter)
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

            if (om.currentHP >= 0) continue; // only when -1

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

    string GetLastStatus() => null;

    string AppendLine(string a, string b)
        => string.IsNullOrEmpty(a) ? b : (a + "\n" + b);

    private void PlayEncounterSfx(MonsterDataSO wild)
    {
        if (AudioManager.I == null || wild == null)
            return;

        if (_currentEncounterIsBoss)
        {
            AudioManager.I.PlaySfx(SfxType.BossEncounter);
            return;
        }

        if (IsShinyMonster(wild))
        {
            AudioManager.I.PlaySfx(SfxType.ShinyEncounter);
            return;
        }

        if (IsUniqueMonster(wild))
        {
            AudioManager.I.PlaySfx(SfxType.UnqiueEncounter);
            return;
        }
    }

    // ========================================================================
    // WIN STREAK SYSTEM
    // ========================================================================

    private void ReconcileHPWithCurrentWinStreak()
    {
        // hook for future
    }

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

    public void SetWinStreak(int value)
    {
        int clamped = Mathf.Max(0, value);

        if (_currentWinStreak == clamped)
            return;

        _currentWinStreak = clamped;

        try
        {
            var data = SaveManager.Data;
            if (data != null)
                data.winStreak = clamped;
        }
        catch { }

        try { GameEvents.WinStreakChanged?.Invoke(clamped); } catch { }

        BattleLogger.Log($"Win streak: {_currentWinStreak}", LogScope.System);
    }

    public int GetWinStreak() => _currentWinStreak;

    private bool IsMonsterDiscovered(MonsterDataSO m)
    {
        if (m == null || string.IsNullOrEmpty(m.id)) return false;
        var data = SaveManager.Data;
        if (data == null) return false;

        data.discoveredMonsterIds ??= new HashSet<string>();
        return data.discoveredMonsterIds.Contains(m.id);
    }

    public bool TryCaptureFromDecision(MonsterDataSO def, int level)
    {
        return TryCatchWithResult(def, level, out _);
    }
}
