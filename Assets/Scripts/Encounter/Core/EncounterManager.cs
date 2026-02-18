using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public partial class EncounterManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Shiny Encounter State
    // ─────────────────────────────────────────────────────────────

    [Header("Shiny Encounters")]
    [Tooltip("Baseline chance for a wild encounter to spawn shiny when no Shiny Orb boost is active.")]
    [SerializeField, Range(0f, 1f)] private float baseWildShinyChance = 0.01f;

    private bool _currentWildIsShiny = false;
    public bool CurrentWildIsShiny => _currentWildIsShiny;

    private bool RollWildShiny(MonsterDataSO wildDef)
    {
        if (!wildDef) return false;

        if (wildDef.shinyIcon == null) return false;

        if (CurrentShinyBoost != null)
            return true;

        float mul = (WorldEventSystem.I != null) ? WorldEventSystem.I.GetWildShinyChanceMultiplier() : 1f;
        float chance = Mathf.Clamp01(baseWildShinyChance * Mathf.Max(0f, mul));
        return Random.value <= chance;
    }
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

    /// <summary>
    /// UI-only helper for previewing how many bonus levels a boss encounter receives.
    /// Mirrors the runtime application (see StartEncounter -> wildLevel adjustment).
    /// </summary>
    public int BossLevelBonusPreview => Mathf.Max(0, bossLevelBonus);

    public int BossLevelBonusPreviewValue() => BossLevelBonusPreview;

    [Header("Wild Titles (Encounter-only)")]
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
    private bool _currentEncounterIsBoss = false;
    private MonsterDataSO _currentBossUsed = null;

    // Cache the most recent battle result (manual hire decision needs this)
    private BattleResult _lastBattleResult;

    private bool inBattle;
    private bool autoMode;

    // Snapshot of auto-mode at battle start. Used to resolve the CURRENT battle (turn pacing/text)
    // even if auto-mode is toggled off mid-battle.
    private bool _autoResolveSnapshot;
    private bool nextEncounterFree;
    private bool autoRunPaidEnergy;

    private Coroutine postResultCo;
    private Coroutine _coBeginBattle;
    private Coroutine autoLoopCo;

    private int _currentWinStreak = 0;
    public int CurrentWinStreak => _currentWinStreak;

    // Tracks whether we are waiting on manual hire decision
    private bool _manualHirePending = false;

    // ─────────────────────────────────────────────────────────
    // Wild Titles (encounter-scoped)
    // ─────────────────────────────────────────────────────────
    private int _wildEncounterSerial = 0;
    private string _wildCombatId = null;
    private TitleSO _wildRolledTitle = null;
    private readonly List<TitleSO> _wildActiveTitles = new List<TitleSO>(8);
    private string _wildTitleLabel = null;
    private bool _lastWildWasShiny = false;

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

        _wildEncounterSerial++;
        string baseId = (wildDef != null && !string.IsNullOrEmpty(wildDef.id)) ? wildDef.id : "UNKNOWN";
        _wildCombatId = $"WILD::{baseId}::{_wildEncounterSerial}";

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
            _currentEncounterIsBoss
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
        case TitleRarity.Rare:   return 40;
        case TitleRarity.Epic:   return 15;
        case TitleRarity.Mythic: return 5;
    }
}
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
        ClearWildTitleInjection();

        if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }
        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        StopAllCoroutines();
    }

    void OnDestroy()
    {
        ClearWildTitleInjection();

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
        StartEncounter(spendEnergy: true);
    }

    // ============================= ENCOUNTER FLOW ===============================

    void StartEncounter(bool spendEnergy)
    {
        ClearWildTitleInjection();

        // World Events placeholder integration
        if (WorldEventSystem.I != null && WorldEventSystem.I.AreEncountersDisabled())
        {
            EmitStatus("Encounters are temporarily suspended.", LogScope.System);
            StopAuto_NoEnergy();
            return;
        }

        // Authority-side eligibility guard (prevents UI drift).
        // NOTE: EncounterPanelUI may allow the encounter button during battle for auto-toggle,
        // but starting a new encounter while in battle is never allowed.
        if (inBattle)
        {
            EmitStatus("Already in battle.", LogScope.System);
            return;
        }

        // Must have a team.
        var preData = SaveManager.Data;
        if (preData == null || preData.team == null || preData.team.Count == 0)
        {
            EmitStatus("No team yet. Catch something to begin!", LogScope.System);
            StopAuto_NoEnergy();
            return;
        }

        // Must have at least one alive team member.
        if (!EligibilityRules.HasMinimumAliveTeam(minMembers: 1))
        {
            EmitStatus("All team members are down. Heal up first.", LogScope.System);
            StopAuto_NoEnergy();
            return;
        }

        if (spendEnergy)
        {
            if (!EligibilityRules.HasRequiredEnergyOrFree(out int needed, out int current))
            {
                StopAuto_NoEnergy();
                EmitStatus($"Need {needed} energy (have {current}).", LogScope.System);
                return;
            }
        }

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

        // World Events: High Alert can increase boss frequency by reducing cadence.
        if (WorldEventSystem.I != null)
        {
            float mul = WorldEventSystem.I.GetBossCadenceMultiplier();
            cadence = Mathf.Max(1, Mathf.RoundToInt(cadence * mul));
        }

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

        ResolveWildTitles(wild, wildLevel);

        _currentWildIsShiny = RollWildShiny(wild);

        EncounterPanelUI.I?.OnWildSpawned(wild);

        PlayEncounterSfx(wild);

        var p = GetFirstAliveTeamMember(data != null ? data.team : null);
        string titleSuffix = string.IsNullOrEmpty(WildTitleLabel) ? "" : $" — {WildTitleLabel}";

        if (_currentEncounterIsBoss)
            EmitStatus($"⚠️ BOSS ENCOUNTER! {wild.displayName} (Lv {wildLevel}){titleSuffix} appears.{((p != null && p.flatAtkBonus > 0) ? $" (+ATK {p.flatAtkBonus})" : "")}");
        else
            EmitStatus($"Encounter! A wild {wild.displayName} (Lv {wildLevel}){titleSuffix} appears.{((p != null && p.flatAtkBonus > 0) ? $" (+ATK {p.flatAtkBonus})" : "")}");

        BattleLogger.BeginEncounter(_currentEncounterIsBoss
            ? $"BOSS: {wild.displayName} Lv{wildLevel}{titleSuffix}"
            : $"{wild.displayName} Lv{wildLevel}{titleSuffix}");

        if (_currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossSpawned?.Invoke(_currentBossUsed.id, _currentBossUsed);

        _autoResolveSnapshot = autoMode;

        inBattle = true;
        OnStateChanged?.Invoke();

        if (!battleManager)
        {
            EmitStatus("No BattleManager assigned.", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            ClearWildTitleInjection();
            return;
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        _manualHirePending = false;

        // Gate Begin() so UI is settled before battle logic starts.
        if (_coBeginBattle != null) StopCoroutine(_coBeginBattle);

        // Deterministic battle RNG: derive a per-battle seed from the active global seed
        // (daily/custom/session) + encounter serial + wild identifiers.
        int battleSeed = BuildBattleSeed(wild, wildLevel, _currentEncounterIsBoss);
        string seedLabel = $"{SeedService.GetDisplaySeedPrefix()}{SeedService.GetDisplaySeedToken()}";

        _coBeginBattle = StartCoroutine(Co_BeginBattleAfterUI(wild, wildLevel, _autoResolveSnapshot, battleSeed, seedLabel));
    }

    private int BuildBattleSeed(MonsterDataSO wild, int level, bool isBoss)
    {
        SeedService.ApplyGlobalSeedForSession();
        int baseSeed = SeedService.ActiveSeed != 0 ? SeedService.ActiveSeed : 1;

        string wildId = (wild != null && !string.IsNullOrEmpty(wild.id)) ? wild.id : "UNKNOWN";
        string raw = $"{baseSeed}|{_wildEncounterSerial}|{(isBoss ? 1 : 0)}|{wildId}|{Mathf.Max(1, level)}";
        int h = StableHash(raw);
        if (h == 0) h = 1;
        return h;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                    hash = hash * 31 + s[i];
            }
            return hash;
        }
    }

    void OnBattleEnded(BattleResult result)
    {
        _lastBattleResult = result;

        Debug.Log($"[EncounterManager] OnBattleEnded incoming result: base={result.creditsBase}, bonus={result.creditsTitleBonus}, totalPreScale={result.creditsGained}, active={result.activeMonsterOwnedId}");

        // Reset encounter-spawn presentation state.
        _lastWildWasShiny = _currentWildIsShiny;
        _currentWildIsShiny = false;

        ClearWildTitleInjection();

        bool escaped = result.escaped;
        bool victory = result.victory;
        bool defeat = !victory && !escaped;

        if (AudioManager.I)
        {
            if (victory) AudioManager.I?.PlaySfx(SfxType.Victory);
            else if (defeat) AudioManager.I?.PlaySfx(SfxType.Defeat);
        }

        int finalcredits = 0;
        int creditTitleBonus = 0;
        if (!escaped)
        {
            finalcredits = ApplyCreditsGainedMultiplier(result.creditsGained);
            finalcredits = Mathf.Max(0, finalcredits);

            // Apply title-based credit multiplier (if any) and compute the explicit title bonus
            try
            {
                // Use the actual active monster from the battle, not just team[0]
                string leadId = !string.IsNullOrEmpty(result.activeMonsterOwnedId) ? result.activeMonsterOwnedId : null;

                if (!string.IsNullOrEmpty(leadId))
                {
                    float cm = TitlesAdapter.GetCreditMultOnVictory(leadId, result.wildDef, result.wildLevel);
                    Debug.Log($"[EncounterManager] Title credit mult for {leadId}: {cm}");
                    if (cm > 0f && cm != 1f)
                    {
                        int withTitles = Mathf.Max(0, Mathf.RoundToInt(finalcredits * cm));
                        creditTitleBonus = Mathf.Max(0, withTitles - finalcredits);
                        finalcredits = withTitles;
                        Debug.Log($"[EncounterManager] Applied title bonus: base={finalcredits - creditTitleBonus}, bonus={creditTitleBonus}, total={finalcredits}");
                    }
                }
            }
            catch (Exception)
            {
                // Safe no-op if TitlesAdapter or Save data is unavailable at runtime
            }

            if (finalcredits > 0)
            {
                if (ResourceManager.I != null)
                {
                    ResourceManager.I?.Add(ResourceType.Credits, finalcredits);
                }
                else
                {
                    ResourceBank.Add(ResourceType.Credits, finalcredits);
                    GameEvents.OnResourcesChanged?.Invoke();
                    GameEvents.ResourceAdded?.Invoke(ResourceType.Credits, finalcredits);
                }
            }
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

        if (_autoResolveSnapshot
            && SaveManager.Data != null
            && FeatureUnlockManager.I != null
            && FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_LogArchive))
        {
            string opponentId = null;
            int opponentLevel = result.wildLevel;
            if (result.wildDef != null) opponentId = result.wildDef.id;

            AutoBattleLogArchive.AddEntry(
                SaveManager.Data,
                opponentId,
                opponentLevel,
                victory,
                escaped,
                BattleLogger.GetLinesSnapshot()
            );
        }

        SaveManager.Save();

        var finished = result;
        finished.creditsGained = finalcredits;

        GameEvents.BattleFinished?.Invoke(finished);
        BattleLogger.EndEncounter(victory);

        bool holdForHireDecision =
            victory &&
            !escaped &&
            !autoMode &&
            !_currentEncounterIsBoss &&
            finished.wildDef != null &&
            !finished.wildDef.uncatchable &&
            EncounterPanelUI.I != null;

        _manualHirePending = holdForHireDecision;

        if (holdForHireDecision)
            PostBattleSummaryManager.I?.SetAutoBattling(true);
        else
            PostBattleSummaryManager.I?.SetAutoBattling(_autoResolveSnapshot);

        int displayCreditsBase = Mathf.Max(0, finalcredits - creditTitleBonus);
        Debug.Log($"[PostBattleSummary] Passing credits: base={displayCreditsBase}, bonus={creditTitleBonus}");
        PostBattleSummaryManager.I?.NotifyBattleEnd(
            finished,
            isAuto: _autoResolveSnapshot,
            growthCoresGained: Mathf.Max(0, result.growthCoresGained),
            monstersLeveledUp: 0,
            captured: false,
            capturedMonsterId: null,
            capturedLevel: 0,
            capturedShiny: false,
            wildWasShiny: _lastWildWasShiny,
            levelUpSummaries: null,
            creditsBase: displayCreditsBase,
            creditsTitleBonus: creditTitleBonus,
            growthCoresBase: result.growthCoresBase,
            growthCoresTitleBonus: result.growthCoresTitleBonus,
            growthCoresDetailLines: null
        );

        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        postResultCo = StartCoroutine(PostResultFlow(victory, escaped));
    }

    private int ApplyCreditsGainedMultiplier(int basecredits)
    {
        if (basecredits <= 0) return 0;
        float mult = 1f;
        if (GameBalance.TryGet(out var bal))
            mult = Mathf.Max(0f, bal.creditGainMultiplier);

        return Mathf.Max(0, Mathf.FloorToInt(basecredits * mult));
    }

    IEnumerator PostResultFlow(bool victory, bool escaped)
    {
        yield return new WaitForSeconds(postResultDelay);
        inBattle = false;
        // IMPORTANT: many UI elements (including Encounter button interactivity)
        // depend on this event to refresh when a battle ends.
        OnStateChanged?.Invoke();

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

            PostBattleSummaryManager.I?.SetAutoBattling(true);

            EncounterPanelUI.I?.ShowHireDecision(_lastBattleResult.wildDef, _lastBattleResult.wildLevel, isShiny: _lastWildWasShiny);
            yield break;
        }

        EmitStatus("Battle finished. Showing summary…", LogScope.System);
        PostBattleSummaryManager.I?.SetAutoBattling(false);
        PostBattleSummaryManager.I?.FlushNowIfPossible();
    }

    public void OnHireDecisionResolved(bool hiredYes, bool captureSucceeded)
    {
        if (!_manualHirePending)
        {
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();
            return;
        }

        _manualHirePending = false;

        if (hiredYes && captureSucceeded && _lastBattleResult.wildDef != null)
        {
            PostBattleSummaryManager.I?.TryUpdateLatestQueuedCapture(
                true,
                _lastBattleResult.wildDef.id,
                _lastBattleResult.wildLevel,
                capturedShiny: _lastWildWasShiny
            );
        }
        else
        {
            PostBattleSummaryManager.I?.TryUpdateLatestQueuedCapture(false, null, 0);
        }

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

    public void ToggleAutoMode()
    {
        autoMode = !autoMode;

        if (autoMode)
        {
            if (!inBattle)
            {
                if (!HasEnergy())
                {
                    EmitStatus("Out of energy!", LogScope.System);
                    autoMode = false;
                    GameEvents.RaiseAutoBattleModeChanged(autoMode);
                    return;
                }

                // Start an encounter immediately in auto-mode and spend energy
                StartEncounter(spendEnergy: true);
            }
            else
            {
                EmitStatus("AUTO mode ON. Will continue after this battle…", LogScope.System);
            }
        }
        else
        {
            EmitStatus("AUTO mode OFF. Tap ENCOUNTER for the next fight.", LogScope.System);
        }

        GameEvents.RaiseAutoBattleModeChanged(autoMode);
    }
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

            if (om.currentHP >= 0) continue;

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

    private void PlayEncounterSfx(MonsterDataSO wild)
    {
        if (AudioManager.I == null || wild == null)
            return;

        if (_currentEncounterIsBoss)
        {
            AudioManager.I.PlaySfx(SfxType.BossEncounter);
            return;
        }

        if (_currentWildIsShiny || IsShinyMonster(wild))
        {
            AudioManager.I?.PlaySfx(SfxType.ShinyEncounter);
            return;
        }

        if (IsUniqueMonster(wild))
        {
            AudioManager.I?.PlaySfx(SfxType.UnqiueEncounter);
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

    public bool RequestForcedEncounter(string monsterId, bool spendEnergy, out string reason)
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

        _currentEncounterIsBoss = false;
        _currentBossUsed = null;

        FieldOpsTracker.RecordEncounter(wild);
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

        ResolveWildTitles(wild, wildLevel);

        _currentWildIsShiny = RollWildShiny(wild);


        EncounterPanelUI.I?.OnWildSpawned(wild);

        PlayEncounterSfx(wild);

        var p = GetFirstAliveTeamMember(data != null ? data.team : null);
        string titleSuffix = string.IsNullOrEmpty(WildTitleLabel) ? "" : $" — {WildTitleLabel}";
        EmitStatus($"Encounter! A wild {wild.displayName} (Lv {wildLevel}){titleSuffix} appears.{((p != null && p.flatAtkBonus > 0) ? $" (+ATK {p.flatAtkBonus})" : "")}");

        BattleLogger.BeginEncounter($"{wild.displayName} Lv{wildLevel}{titleSuffix}");

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
        // Gate Begin() so UI is settled before battle logic starts.
        if (_coBeginBattle != null) StopCoroutine(_coBeginBattle);

        int battleSeed = BuildBattleSeed(wild, wildLevel, false);
        string seedLabel = $"{SeedService.GetDisplaySeedPrefix()}{SeedService.GetDisplaySeedToken()}";

        _coBeginBattle = StartCoroutine(Co_BeginBattleAfterUI(wild, wildLevel, false, battleSeed, seedLabel));
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
    // ─────────────────────────────────────────────────────────
    // Battle gating helpers (UI must refresh before Begin)
    // ─────────────────────────────────────────────────────────

    private OwnedMonsterData GetFirstAliveTeamMember(List<OwnedMonsterData> team)
    {
        if (team == null) return null;

        for (int i = 0; i < team.Count; i++)
        {
            var m = team[i];
            if (m == null) continue;
            if (string.IsNullOrEmpty(m.monsterId)) continue;

            // Alive only. KO monsters are ignored for encounter messaging and battle eligibility.
            if (m.currentHP > 0)
                return m;
        }

        return null;
    }

    private IEnumerator Co_BeginBattleAfterUI(MonsterDataSO wild, int wildLevel, bool autoResolveSnapshot, int battleSeed, string seedLabel)
    {
        // Ensure the encounter UI reflects the latest team state (KO filtering / slide-down preview).
        EncounterPanelUI.I?.RefreshAll();

        // Give Unity one frame so layout rebuilds, TMP updates, and any previous UI transitions finish.
        yield return null;

        // If the player has no battle-ready monsters, do not start a battle.
        var data = SaveManager.Data;
        if (data == null || data.team == null)
        {
            EmitStatus("No team assigned.", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            ClearWildTitleInjection();
            yield break;
        }

        int readyCount = 0;
        for (int i = 0; i < data.team.Count; i++)
        {
            var m = data.team[i];
            if (m == null) continue;
            if (string.IsNullOrEmpty(m.monsterId)) continue;
            if (m.currentHP > 0) readyCount++;
        }

        if (readyCount <= 0)
        {
            EmitStatus("No battle-ready monsters (all are KO).", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            ClearWildTitleInjection();
            yield break;
        }

        if (!battleManager)
        {
            EmitStatus("No BattleManager assigned.", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            ClearWildTitleInjection();
            yield break;
        }

        // Deterministic battle RNG (BattleManager owns the battle RNG).
        battleManager.SetBattleSeed(battleSeed, seedLabel);

        // Begin the battle after UI is settled.
        battleManager.Begin(wild, wildLevel, OnBattleEnded);

        // Auto-mode is handled by the battle loop; we just snapshot the intent here.
    }
}