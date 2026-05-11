using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

// ─────────────────────────────────────────────────────────────
// RiftManager.Core
// Shared fields, inspector config, wild title state, win streak, dev overrides.
// ─────────────────────────────────────────────────────────────

public partial class RiftManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Premium Rift State
    // ─────────────────────────────────────────────────────────────

    [Header("Premium Rifts")]
    [Tooltip("Baseline chance for a wild rift to spawn premium when no Premium Orb boost is active.")]
    [SerializeField, Range(0f, 1f)] private float baseWildPremiumChance = 0.01f;

    private bool _currentWildIsPremium = false;
    public bool CurrentWildIsPremium => _currentWildIsPremium;

    private bool RollWildPremium(MonsterDataSO wildDef)
    {
        if (!wildDef) return false;

        if (wildDef.premiumIcon == null) return false;

        if (CurrentPremiumBoost != null)
            return true;

        float mul = (WorldEventSystem.I != null) ? WorldEventSystem.I.GetWildPremiumChanceMultiplier() : 1f;
        float chance = Mathf.Clamp01(baseWildPremiumChance * Mathf.Max(0f, mul));
        return Random.value <= chance;
    }
    public static RiftManager I { get; private set; }

    [Obsolete("UI no longer renders inline status. Use BattleLogger instead.")]
    public event Action<string> OnStatus;
    public event Action OnStateChanged;
    public static event Action<int, int> OnEnergyGained;

    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;

    [Header("Boss Settings")]
    [Tooltip("0 = use PlayerData.bossEveryN")]
    [SerializeField, Min(0)] private int bossEveryNOverride = 0;
    [Tooltip("Flat level bonus applied to boss rifts")]
    [SerializeField, Min(0)] private int bossLevelBonus = 2;

    /// <summary>
    /// UI-only helper for previewing how many bonus levels a boss rift receives.
    /// Mirrors the runtime application (see StartRift -> wildLevel adjustment).
    /// </summary>
    public int BossLevelBonusPreview => Mathf.Max(0, bossLevelBonus);

    public int BossLevelBonusPreviewValue() => BossLevelBonusPreview;

    public void PrepareBattleHide() => battleManager?.SetBattleRevealObjectsInactive();

    [Header("Wild Titles (Rift-only)")]
    [SerializeField, Range(0f, 1f)] private float wildTitleRollChance = 0.35f;
    [SerializeField] private string unemployedLabel = "Unemployed";

    [Header("Options")]
    [SerializeField] private float postResultDelay = 0.8f;
    [SerializeField] private float autoPollSeconds = 0.25f;

    [Header("Battle Spawn Points")]
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform enemySpawnPoint;

    public Transform PlayerSpawnPoint => playerSpawnPoint;
    public Transform EnemySpawnPoint => enemySpawnPoint;

    // Runtime state
    private bool _currentRiftIsBoss = false;
    private MonsterDataSO _currentBossUsed = null;

    // Cache the most recent battle result (manual hire decision needs this)
    private BattleResult _lastBattleResult;

    private bool inBattle;
    private bool autoMode;

    // Snapshot of auto-mode at battle start. Used to resolve the CURRENT battle (turn pacing/text)
    // even if auto-mode is toggled off mid-battle.
    private bool _autoResolveSnapshot;
    private bool nextRiftFree;
    private bool autoRunPaidEnergy;

    private Coroutine postResultCo;
    private Coroutine autoLoopCo;

    private int _currentWinStreak = 0;
    public int CurrentWinStreak => _currentWinStreak;

    // Tracks whether we are waiting on manual hire decision
    private bool _manualHirePending = false;

    // ─────────────────────────────────────────────────────────
    // Wild Titles (rift-scoped)
    // ─────────────────────────────────────────────────────────
    private int _wildRiftSerial = 0;
    private string _wildCombatId = null;
    private TitleSO _wildRolledTitle = null;
    private readonly List<TitleSO> _wildActiveTitles = new List<TitleSO>(8);
    private string _wildTitleLabel = null;
    private bool _lastWildWasPremium = false;

    public string WildCombatId => _wildCombatId;
    public TitleSO WildRolledTitle => _wildRolledTitle;
    public IReadOnlyList<TitleSO> WildActiveTitles => _wildActiveTitles;

    // Existing behavior: returns unemployedLabel if empty/null
    public string WildTitleLabel => string.IsNullOrEmpty(_wildTitleLabel) ? unemployedLabel : _wildTitleLabel;

    // NEW: UI helper. If the wild monster has no real title, returns false.
    // This is what the UI should use to hide the TitleLabel GameObject.
    public bool WildHasTitle
    {
        get
        {
            if (string.IsNullOrEmpty(_wildTitleLabel)) return false;
            return !string.Equals(_wildTitleLabel, unemployedLabel, StringComparison.OrdinalIgnoreCase);
        }
    }

    // NEW: UI-safe label. Empty string means "hide title UI".
    public string WildTitleLabelUI => WildHasTitle ? _wildTitleLabel : "";

    private void ClearWildTitleInjection()
    {
        if (!string.IsNullOrEmpty(_wildCombatId))
            TitlesAdapter.ClearLocalTitles(_wildCombatId);

        _wildCombatId = null;
        _wildRolledTitle = null;
        _wildActiveTitles.Clear();
        _wildTitleLabel = null;
    }

    private void ResolveWildTitles(MonsterDataSO wildDef, int wildLevel)
    {
        ClearWildTitleInjection();

        _wildRiftSerial++;
        string baseId = (wildDef != null && !string.IsNullOrEmpty(wildDef.id)) ? wildDef.id : "UNKNOWN";
        _wildCombatId = $"WILD::{baseId}::{_wildRiftSerial}";

        // Always-on (species identity)
        if (wildDef != null && wildDef.defaultAlwaysOnTitles != null)
        {
            for (int i = 0; i < wildDef.defaultAlwaysOnTitles.Length; i++)
            {
                var t = wildDef.defaultAlwaysOnTitles[i];
                if (t != null && !_wildActiveTitles.Contains(t))
                    _wildActiveTitles.Add(t);
            }
        }

        // Candidate pool from TitleTrack tiers
        var candidates = new List<TitleSO>(12);

        if (wildDef != null && wildDef.titleTrack != null && wildDef.titleTrack.tiers != null)
        {
            var seen = new HashSet<TitleSO>();
            for (int ti = 0; ti < wildDef.titleTrack.tiers.Count; ti++)
            {
                var tier = wildDef.titleTrack.tiers[ti];
                if (tier == null) continue;

                if (wildLevel < Mathf.Max(1, tier.levelRequired))
                    continue;

                var choices = tier.unlockChoices;
                if (choices == null) continue;

                for (int ci = 0; ci < choices.Count; ci++)
                {
                    var title = choices[ci];
                    if (title == null) continue;
                    if (!title.canRollOnWild) continue;
                    if (seen.Add(title))
                        candidates.Add(title);
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        TitleSO forced = null;
        string forcedId = Dev_ForceWildTitleId;

        if (!string.IsNullOrWhiteSpace(forcedId))
        {
            forcedId = forcedId.Trim();

            if (TitleManager.I != null)
                forced = TitleManager.I.GetTitleById(forcedId);

            if (forced == null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var t = candidates[i];
                    if (t == null) continue;

                    if (string.Equals(t.titleId, forcedId, StringComparison.OrdinalIgnoreCase))
                    {
                        forced = t;
                        break;
                    }
                }
            }

            if (forced != null)
            {
                _wildRolledTitle = forced;
                if (!_wildActiveTitles.Contains(_wildRolledTitle))
                    _wildActiveTitles.Add(_wildRolledTitle);

                _wildTitleLabel = _wildRolledTitle.DisplayOrId;

                TitlesAdapter.SetLocalTitles(_wildCombatId, _wildActiveTitles);
                return; 
            }
            else
            {
                _wildTitleLabel = $"(Missing Title: {forcedId})";
                TitlesAdapter.SetLocalTitles(_wildCombatId, _wildActiveTitles);
                return;
            }
        }
#endif

        bool shouldRoll =
            _currentRiftIsBoss
                ? (candidates.Count > 0)
                : (candidates.Count > 0 && Random.value <= Mathf.Clamp01(wildTitleRollChance));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Dev_ForceWildTitleRoll && candidates.Count > 0)
            shouldRoll = true;
#endif

        if (shouldRoll)
        {
            _wildRolledTitle = PickWildTitleWeighted(candidates);
            if (_wildRolledTitle != null && !_wildActiveTitles.Contains(_wildRolledTitle))
                _wildActiveTitles.Add(_wildRolledTitle);
        }
        else
        {
            _wildRolledTitle = null;
        }

        _wildTitleLabel = (_wildRolledTitle != null) ? _wildRolledTitle.DisplayOrId : unemployedLabel;

        TitlesAdapter.SetLocalTitles(_wildCombatId, _wildActiveTitles);
    }

    

    private TitleSO PickWildTitleWeighted(List<TitleSO> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;

        int total = 0;
        for (int i = 0; i < candidates.Count; i++)
            total += GetRarityWeight(candidates[i]);

        if (total <= 0)
            return candidates[Random.Range(0, candidates.Count)];

        int roll = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            acc += GetRarityWeight(candidates[i]);
            if (roll < acc)
                return candidates[i];
        }
        return candidates[candidates.Count - 1];
    }

    private int GetRarityWeight(TitleSO t)
    {
        if (!t) return 0;
        switch (t.Rarity)
        {
            default:
            case TitleRarity.Common: return 100;
            case TitleRarity.Rare: return 40;
            case TitleRarity.Epic: return 15;
            case TitleRarity.Mythic: return 5;
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────

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

    public bool RequestForcedRift(string monsterId, bool spendEnergy, out string reason)
    {
        reason = null;

        if (inBattle) { reason = "Already in battle."; return false; }

        var data = SaveManager.Data;
        if (data == null || data.team == null || data.team.Count == 0)
        {
            reason = "No team yet. Catch something to begin!";
            StopAuto_NoEnergy();
            return false;
        }

        if (!HasHealthyMonsters())
        {
            reason = "All team members are down. Heal up first.";
            StopAuto_NoEnergy();
            return false;
        }

        if (string.IsNullOrWhiteSpace(monsterId))
        {
            reason = "Monster ID is empty.";
            return false;
        }

        monsterId = monsterId.Trim();
        MonsterDataSO wild = MonsterLibraryLocator.GetById(monsterId);
        if (wild == null)
        {
            reason = $"Monster '{monsterId}' not found.";
            return false;
        }

        if (spendEnergy)
        {
            if (!HasEnergy()) { reason = "Out of energy!"; return false; }
            if (!SpendEnergy()) { reason = "Out of energy!"; return false; }
        }

        _currentRiftIsBoss = false;
        _currentBossUsed = null;

        FieldOpsTracker.RecordRift(wild);
        NotifyAuto_SpecialSpawn(wild);

        int wildLevel = Mathf.Clamp(CalculateAverageTeamLevel() + Random.Range(-1, 2), 1, 99);

        ResolveWildTitles(wild, wildLevel);

        _currentWildIsPremium = RollWildPremium(wild);
        string wildName = MonsterNameFormatter.Format(wild, _currentWildIsPremium);


        RiftPanelUI.I?.OnWildSpawned(wild);

        PlayRiftSfx(wild);

        var p = data.team[0];
        string titleSuffix = string.IsNullOrEmpty(WildTitleLabel) ? "" : $" — {WildTitleLabel}";
        EmitStatus($"Rift! A wild {wildName} (Lv {wildLevel}){titleSuffix} appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");

        BattleLogger.BeginRift($"{wildName} Lv{wildLevel}{titleSuffix}");

        inBattle = true;
        OnStateChanged?.Invoke();

        if (!battleManager)
        {
            reason = "No BattleManager assigned.";
            inBattle = false;
            OnStateChanged?.Invoke();
            ClearWildTitleInjection();
            return false;
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        _manualHirePending = false;

        // Safety: ensure regular battles never inherit Iron HUD bindings.
        EnsureNonIronHudBindings();

        battleManager.Begin(wild, wildLevel, OnBattleEnded);
        return true;
    }



    // ─────────────────────────────────────────────────────────
    // DEV / TEST OVERRIDES (PlayerPrefs driven)
    // ─────────────────────────────────────────────────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const string PP_ForceWildTitleRoll = "DEV_ForceWildTitleRoll"; // int 0/1
    private const string PP_ForceWildTitleId = "DEV_ForceWildTitleId";     // string e.g. "T-001"

    public bool Dev_ForceWildTitleRoll
    {
        get => PlayerPrefs.GetInt(PP_ForceWildTitleRoll, 0) == 1;
        set { PlayerPrefs.SetInt(PP_ForceWildTitleRoll, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public string Dev_ForceWildTitleId
    {
        get => PlayerPrefs.GetString(PP_ForceWildTitleId, "");
        set { PlayerPrefs.SetString(PP_ForceWildTitleId, value ?? ""); PlayerPrefs.Save(); }
    }
#endif
}