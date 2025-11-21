using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;
using System;
using System.Reflection;

public class EncounterManager : MonoBehaviour
{
    public static EncounterManager I { get; private set; }

    [Obsolete("UI no longer renders inline status. Use BattleLogger instead.")]
    public event Action<string> OnStatus;
    public event Action OnStateChanged;
    public static event Action<int, int> OnEnergyGained; 

    [Header("Refs")]
    [SerializeField] private BattleManager battleManager;

    [SerializeField] private MonoBehaviour blindersBehaviour;
    MethodInfo _blindersShow, _blindersHide;

    [Header("Boss Settings")]
    [Tooltip("0 = use PlayerData.bossEveryN")]
    [SerializeField, Min(0)] private int bossEveryNOverride = 0;
    [Tooltip("Flat level bonus applied to boss encounters")]
    [SerializeField, Min(0)] private int bossLevelBonus = 2;

    [Header("Options")]
    [SerializeField] private float postResultDelay = 0.8f;
    [SerializeField] private float autoPollSeconds = 0.25f;

    // ───────────────────────── Energy (regen over time) ─────────────────────────
    [Header("Energy (Regen)")]
    [Tooltip("If SaveManager.Data has encounterMax/Cost, those win; otherwise these are used.")]
    [SerializeField, Min(1)] private int fallbackEncounterMax = 10;
    [SerializeField, Min(1)] private int fallbackEncounterCost = 1;
    [Tooltip("Seconds required to regenerate 1 energy point.")]
    [SerializeField, Min(1f)] private float energySecondsPerPoint = 3600f;

    const string PP_ENERGY_POINTS = "ENERGY_points";
    const string PP_ENERGY_LAST   = "ENERGY_lastUnix";
    const string PP_ENERGY_REM    = "ENERGY_remainder";

    int   _energyPoints;
    long  _energyLastUnix;
    float _energyRemainderSecs;
    float _tickAccum;

    // ───────────────────────── Runtime ─────────────────────────
    private bool _currentEncounterIsBoss = false;
    private MonsterDataSO _currentBossUsed = null;

    private bool inBattle;
    private bool autoMode;
    private bool nextEncounterFree;
    private bool autoRunPaidEnergy;

    private Coroutine postResultCo;
    private Coroutine autoLoopCo;

    // ── Win streak (persist if present on save) ─────────────────────────────────
    private int _currentWinStreak = 0;
    public int CurrentWinStreak => _currentWinStreak;

    // ===========================================================================

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        // cache blinders reflection (soft-optional dependency)
        if (blindersBehaviour)
        {
            var t = blindersBehaviour.GetType();
            _blindersShow = t.GetMethod("Show", BindingFlags.Public | BindingFlags.Instance);
            _blindersHide = t.GetMethod("Hide", BindingFlags.Public | BindingFlags.Instance);
        }

        _currentWinStreak = LoadWinStreakOr(0);
        _currentWinStreak = Mathf.Max(0, _currentWinStreak);

        // energy: load + offline regen
        LoadEnergy();
        ApplyOfflineRegen();
        MirrorEnergyIntoSaveData(); // keep SaveManager.Data.encounterPoints in sync if present
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
        SaveManager.LoadOrCreate();
        SaveManager.Data.EnsureTransientSets();
        GlobalEffects.RecalcShinySynergy();

        inBattle = false;
        autoMode = false;
        nextEncounterFree = false;
        autoRunPaidEnergy = false;

        ResourceBank.EnsureSize();

        // Manual mode by default → summaries are allowed to pop immediately
        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("Tap ENCOUNTER to begin. Hold to toggle AUTO.", LogScope.System);
        OnStateChanged?.Invoke();

        NormalizeTeamHPIfUninitialized();
        GameEvents.WinStreakChanged?.Invoke(_currentWinStreak);

        // Ensure blinders start UP on the Encounter panel
        Blinders_Show();

        // initial UI update with correct energy mirrored
        MirrorEnergyIntoSaveData();
        GameEvents.EnergyChanged?.Invoke();
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

            if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }
            autoLoopCo = StartCoroutine(AutoLoop());

            // While AUTO is on, summaries queue up and won’t pop until energy depletes.
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

            // AUTO off → allow summaries to appear for subsequent battles
            PostBattleSummaryManager.I?.SetAutoBattling(false);

            EmitStatus("AUTO mode OFF. Tap ENCOUNTER for the next fight.", LogScope.System);
        }

        OnStateChanged?.Invoke();
    }

    // =============================== ENERGY ====================================

    public int  GetEnergyPoints()      => _energyPoints;
    public int  GetEncounterMax()      => (SaveManager.Data != null && SaveManager.Data.encounterMax > 0)
                                            ? SaveManager.Data.encounterMax : fallbackEncounterMax;
    public int  GetEncounterCost()     => (SaveManager.Data != null && SaveManager.Data.encounterCost > 0)
                                            ? SaveManager.Data.encounterCost : fallbackEncounterCost;
    public bool HasEnergy()            => _energyPoints >= GetEncounterCost();

    // Exact time until full (accounts for current partial remainder)
    public int GetSecondsUntilFull()
    {
        int max = GetEncounterMax();
        if (_energyPoints >= max) return 0;
        int missing = max - _energyPoints;
        double total = (missing * energySecondsPerPoint) - _energyRemainderSecs;
        return Mathf.Max(0, (int)Math.Ceiling(total));
    }

    // Utility for scripts that just want to add energy (e.g., rewards)
    public void AddEnergy(int amount, bool allowOvercap = true)
    {
       int max = GetEncounterMax();
        int before = _energyPoints;

        _energyPoints += amount;
        if (!allowOvercap) _energyPoints = Mathf.Min(_energyPoints, max);
        ClampEnergy();

        int gained = Mathf.Max(0, _energyPoints - before);

        SaveEnergy();
        MirrorEnergyIntoSaveData();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();

        if (gained > 0)
            OnEnergyGained?.Invoke(gained, _energyPoints);
    }

    // Spends energy and resets the regen timer (your “timer restarts” spec)
    public bool SpendEnergy()
    {
        int cost = GetEncounterCost();
        if (_energyPoints < cost) return false;

        _energyPoints -= cost;
        ClampEnergy();

        // reset countdown
        _energyLastUnix = NowUnix();
        _energyRemainderSecs = 0f;

        SaveEnergy();
        MirrorEnergyIntoSaveData();
        GameEvents.EnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    // For external callers that just want a try-spend
    public bool SpendEnergyIfPossible() => SpendEnergy();

    // ---- core storage / runtime ticking ----
    void LoadEnergy()
    {
        int max = GetEncounterMax();

        // default to full when first run or missing keys
        int def = Mathf.Clamp(max, 1, 9999);
        _energyPoints        = PlayerPrefs.GetInt(PP_ENERGY_POINTS, def);
        string lastStr       = PlayerPrefs.GetString(PP_ENERGY_LAST, NowUnix().ToString());
        _energyLastUnix      = long.Parse(lastStr);
        _energyRemainderSecs = PlayerPrefs.GetFloat(PP_ENERGY_REM, 0f);

        ClampEnergy();
        _energyRemainderSecs = Mathf.Clamp(_energyRemainderSecs, 0f, Mathf.Max(0f, energySecondsPerPoint - 0.001f));
    }

    void SaveEnergy()
    {
        PlayerPrefs.SetInt(PP_ENERGY_POINTS, _energyPoints);
        PlayerPrefs.SetString(PP_ENERGY_LAST, _energyLastUnix.ToString());
        PlayerPrefs.SetFloat(PP_ENERGY_REM, _energyRemainderSecs);
        PlayerPrefs.Save();
    }

    void ClampEnergy()
    {
        int max = GetEncounterMax();
        _energyPoints = Mathf.Clamp(_energyPoints, 0, Mathf.Max(1, max));
    }

    void ApplyOfflineRegen()
    {
        int max = GetEncounterMax();
        if (_energyPoints >= max) { _energyRemainderSecs = 0f; return; }

        long elapsed = NowUnix() - _energyLastUnix;
        if (elapsed <= 0) return;

        double total = _energyRemainderSecs + elapsed;
        int gained = (int)Math.Floor(total / energySecondsPerPoint);
        _energyRemainderSecs = (float)(total - (gained * energySecondsPerPoint));

       if (gained > 0)
        {
            _energyPoints = Mathf.Min(max, _energyPoints + gained);
            if (_energyPoints >= max) _energyRemainderSecs = 0f;

            MirrorEnergyIntoSaveData();
            SaveEnergy();
            GameEvents.EnergyChanged?.Invoke();

            OnEnergyGained?.Invoke(gained, _energyPoints);
        }

        // move “last” to now so future elapsed is clean
        _energyLastUnix = NowUnix();
        SaveEnergy();
    }

    void TickEnergyRuntime()
    {
        int max = GetEncounterMax();
        if (_energyPoints >= max) return;

        _tickAccum += Time.unscaledDeltaTime;
        if (_tickAccum < 1f) return;
        _tickAccum = 0f;

        _energyRemainderSecs += 1f;
       if (_energyRemainderSecs >= energySecondsPerPoint)
        {
            _energyRemainderSecs -= energySecondsPerPoint;
            int before = _energyPoints;
            _energyPoints = Mathf.Min(max, _energyPoints + 1);
            if (_energyPoints >= max) _energyRemainderSecs = 0f;

            MirrorEnergyIntoSaveData();
            SaveEnergy();
            GameEvents.EnergyChanged?.Invoke();
            OnStateChanged?.Invoke();

            int gained = Mathf.Max(0, _energyPoints - before);
            if (gained > 0)
                OnEnergyGained?.Invoke(gained, _energyPoints);
        }
    }

    void MirrorEnergyIntoSaveData()
    {
        if (SaveManager.Data == null) return;
        // Keep SaveManager’s encounterPoints in sync for any legacy UI that reads it
        SaveManager.Data.encounterPoints = _energyPoints;
        // Do NOT touch encounterMax/Cost (they may be tuned elsewhere)
        SaveManager.Save();
    }

    static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ============================== AUTO LOOP ==================================

    IEnumerator AutoLoop()
    {
        while (autoMode)
        {
            if (!inBattle)
            {
                if (!HasHealthyMonsters())
                {
                    EmitStatus("AUTO stopped: no healthy team members.", LogScope.System);
                    StopAuto_NoEnergy();
                    yield break;
                }

                if (!autoRunPaidEnergy)
                {
                    if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                    if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                    autoRunPaidEnergy = true;
                }

                StartEncounter(false);
            }

            yield return new WaitForSeconds(autoPollSeconds);
        }
    }

    void StopAuto_NoEnergy()
    {
        if (!autoMode) return;
        autoMode = false;
        autoRunPaidEnergy = false;

        IdleBattleManager.I?.DisableAuto();

        if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }

        // Out of energy → let summaries pop now.
        PostBattleSummaryManager.I?.NotifyEnergyDepleted();
        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("AUTO stopped: no energy.", LogScope.System);
        OnStateChanged?.Invoke();

        // Show blinders on the encounter screen (the summary will be on top of another panel)
        Blinders_Show();
    }

    // ============================= ENCOUNTER FLOW ===============================

    void StartEncounter(bool spendEnergy)
    {
        if (SaveManager.Data.team == null || SaveManager.Data.team.Count == 0)
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

        var d = SaveManager.Data;
        int cadence = (bossEveryNOverride > 0) ? bossEveryNOverride : (d != null && d.bossEveryN > 0 ? d.bossEveryN : 10);

        _currentEncounterIsBoss = ShouldSpawnBoss(d != null ? d.encountersSinceBoss : 0, cadence);
        _currentBossUsed = null;

        MonsterDataSO wild = null;

        if (_currentEncounterIsBoss)
        {
            var lib = MonsterLibraryLocator.Lib;
            _currentBossUsed = PickBossWeighted(lib, d != null ? d.lastBossId : null);

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

        int avgTeamLvl = 1;
        if (SaveManager.Data.team != null && SaveManager.Data.team.Count > 0)
        {
            int sum = 0;
            for (int i = 0; i < SaveManager.Data.team.Count; i++)
                sum += SaveManager.Data.team[i].level;
            avgTeamLvl = Mathf.Max(1, Mathf.RoundToInt((float)sum / SaveManager.Data.team.Count));
        }
        int wildLevel = Mathf.Clamp(avgTeamLvl + Random.Range(-1, 2), 1, 99);
        if (_currentEncounterIsBoss)
            wildLevel = Mathf.Max(1, wildLevel + bossLevelBonus);

        PlayEncounterSfx(wild);

        if (TapBoost.I) TapBoost.I.ResetEncounter();

        var p = SaveManager.Data.team[0];
        if (_currentEncounterIsBoss)
            EmitStatus($"⚠️ BOSS ENCOUNTER! {wild.displayName} (Lv {wildLevel}) appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");
        else
            EmitStatus($"Encounter! A wild {wild.displayName} (Lv {wildLevel}) appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");

        // Log lifecycle
        BattleLogger.BeginEncounter(_currentEncounterIsBoss ? $"BOSS: {wild.displayName} Lv{wildLevel}" : $"{wild.displayName} Lv{wildLevel}");

        if (_currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossSpawned?.Invoke(_currentBossUsed.id, _currentBossUsed);

        // Battle starts → lower the blinders
        Blinders_Hide();

        inBattle = true;
        OnStateChanged?.Invoke();

        if (!battleManager)
        {
            EmitStatus("No BattleManager assigned.", LogScope.System);
            inBattle = false;
            OnStateChanged?.Invoke();
            // Make sure blinders are up again in this error state
            Blinders_Show();
            return;
        }

        battleManager.Begin(wild, wildLevel, OnBattleEnded);
    }

    void OnBattleEnded(BattleResult result)
    {

        if (AudioManager.I)
        {
            if (result.victory)
                AudioManager.I.PlaySfx(SfxType.Victory);
            else
                AudioManager.I.PlaySfx(SfxType.Defeat);
        }
        
        // Apply any local/global (non-title) encounter multipliers here
        int finalCoins = ApplyCoinsGainedMultiplier(result.coinsGained);
        finalCoins = Mathf.Max(0, finalCoins);

        // Route coins through ResourceManager (this actually adds them)
        if (finalCoins > 0)
            ResourceManager.I.Add(ResourceType.Coins, finalCoins);

        // Emit a human-readable status with the final actually-banked coins
        EmitStatus(result.victory ? $"Victory! +{finalCoins} coins" : "Defeat.");

        // Boss defeat signal
        if (result.victory && _currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossDefeated?.Invoke(_currentBossUsed.id);

        // Cadence / last-boss bookkeeping
        if (SaveManager.Data != null)
        {
            AfterBattleCadenceUpdate(
                ref SaveManager.Data.encountersSinceBoss,
                _currentEncounterIsBoss,
                _currentBossUsed,
                ref SaveManager.Data.lastBossId
            );
        }

        // Attempt capture (except bosses/uncatchables) on victory
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

        // Win streak update (+ event + save-back if supported)
        if (result.victory) SetWinStreak(_currentWinStreak + 1);
        else                SetWinStreak(0);

        ReconcileHPWithCurrentWinStreak();

        OnStateChanged?.Invoke();

        // Persist non-resource state changes; coins are already saved by ResourceManager
        SaveManager.Save();

        // Broadcast a normalized battle finished event with the real coin value we credited
        var finished = result;
        finished.coinsGained = finalCoins;
        GameEvents.BattleFinished?.Invoke(finished);

        // Close out encounter in log
        BattleLogger.EndEncounter(result.victory);

        // Continue post-result flow (this handles AUTO chaining vs. summary popup)
        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        postResultCo = StartCoroutine(PostResultFlow(result.victory));
    }

    string GetLastStatus() => null; // kept for AppendLine compatibility
    string AppendLine(string a, string b) => string.IsNullOrEmpty(a) ? b : (a + "\n" + b);

    private int ApplyCoinsGainedMultiplier(int baseCoins)
    {
        if (baseCoins <= 0) return 0;
        const float MULT = 1f;
        return Mathf.Max(0, Mathf.FloorToInt(baseCoins * MULT));
    }

    IEnumerator PostResultFlow(bool victory)
    {
        yield return new WaitForSeconds(postResultDelay);
        inBattle = false;

        // Always clear “pay-once per auto-chain” flag after a battle ends
        if (!victory)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                // AUTO: keep chugging; summaries stay queued until energy ends or AUTO is turned off.
                EmitStatus("Defeat. Retrying (AUTO)…", LogScope.System);
                // No summary popup now — leave it queued.
                yield break;
            }

            // MANUAL: immediately show summary.
            EmitStatus("Battle finished. Showing summary…", LogScope.System);
            PostBattleSummaryManager.I?.SetAutoBattling(false);
            PostBattleSummaryManager.I?.FlushNowIfPossible();   // <-- show now
            yield break;
        }

        // ===== Victory =====
        if (autoMode)
        {
            // AUTO: do not show summary; keep chaining battles using the paid energy from before.
            if (!autoRunPaidEnergy)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                autoRunPaidEnergy = true;
            }
            StartEncounter(false); // chain for free during AUTO
            yield break;
        }

        // MANUAL: show summary immediately, then return to Encounter panel (with blinders up).
        nextEncounterFree = true;                  // your “next is free” rule still applies
        OnStateChanged?.Invoke();
        EmitStatus("Battle finished. Showing summary…", LogScope.System);

        PostBattleSummaryManager.I?.SetAutoBattling(false);
        PostBattleSummaryManager.I?.FlushNowIfPossible();       // <-- show now
    }

    // ================= LURES / LUCK / SHINY / CAPTURE BAND =====================

    public IReadOnlyList<LureBiasData> ActiveLures => SaveManager.Data?.activeLures;

    public void AddLure(MonsterType type, float bonus = 0.30f, int durationHours = 2)
    {
        if (SaveManager.Data == null) return;

        bonus = Mathf.Clamp(bonus, 0f, 2f);
        durationHours = Mathf.Max(1, durationHours);

        long now = SaveManager.NowUnix();
        long expiry = now + durationHours * 3600L;

        if (SaveManager.Data.activeLures == null)
            SaveManager.Data.activeLures = new List<LureBiasData>();

        SaveManager.Data.activeLures.Clear();
        SaveManager.Data.activeLures.Add(new LureBiasData
        {
            type = type,
            bonus = bonus,
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    private Dictionary<MonsterType, float> BuildLureTypeMultipliers()
    {
        var map = new Dictionary<MonsterType, float>();
        var cur = CurrentLure;
        if (cur == null) return map;

        float mult = Mathf.Clamp(1f + Mathf.Max(0f, cur.bonus), 1f, 3f);
        map[cur.type] = mult;
        return map;
    }

    public MonsterDataSO PickWildConsideringLures()
    {
        var lib = MonsterLibraryLocator.Lib;
        if (lib == null || lib.monsters == null || lib.monsters.Length == 0)
            return null;

        List<MonsterDataSO> pool = new List<MonsterDataSO>(lib.monsters.Length);
        for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (m == null || string.IsNullOrEmpty(m.id)) continue;
            if (m.spawnWeight <= 0f) continue;
            pool.Add(m);
        }

        if (pool.Count == 0)
        {
            for (int i = 0; i < lib.monsters.Length; i++)
            {
                var m = lib.monsters[i];
                if (m != null && !string.IsNullOrEmpty(m.id)) pool.Add(m);
            }
            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        var typeMult = BuildLureTypeMultipliers();
        float luckBonus01 = GetActiveLuckBonus01();

        float minBase = float.MaxValue;
        float maxBase = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            float b = Mathf.Max(0f, pool[i].spawnWeight);
            if (b < minBase) minBase = b;
            if (b > maxBase) maxBase = b;
        }

        float GetFinalWeight(MonsterDataSO m)
        {
            float baseW = Mathf.Max(0f, m.spawnWeight);
            if (baseW <= 0f) return 0f;

            float mult = 1f;

            if (typeMult != null && typeMult.TryGetValue(m.type, out float tMult))
                mult *= Mathf.Max(0f, tMult);

            mult *= JobBalance.GetWyrmDenRarityWeightMult(m.rarity);

            if (luckBonus01 > 0f && maxBase > minBase)
            {
                float scarcity01 = Mathf.Clamp01((maxBase - baseW) / (maxBase - minBase));
                float luckMult = 1f + luckBonus01 * scarcity01;
                mult *= luckMult;
            }

            float finalW = baseW * mult;
            if (float.IsNaN(finalW) || float.IsInfinity(finalW)) return 0f;
            return Mathf.Max(0f, finalW);
        }

        return PickByWeight(pool, GetFinalWeight);
    }

    public LureBiasData CurrentLure
    {
        get
        {
            var list = SaveManager.Data?.activeLures;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private LuckBoostData CurrentLuck
    {
        get
        {
            var list = SaveManager.Data?.activeLuckBoosts;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private ShinyBoostData CurrentShinyBoost
    {
        get
        {
            var list = SaveManager.Data?.activeShinyBoosts;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private CaptureBandData CurrentCaptureBand
    {
        get
        {
            var list = SaveManager.Data?.activeCaptureBands;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private float GetActiveLuckBonus01()
    {
        var cur = CurrentLuck;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    private float GetActiveShinyBoostMult()
    {
        var cur = CurrentShinyBoost;
        if (cur == null) return 1f;
        return Mathf.Max(1f, cur.bonus);
    }

    private float GetActiveCaptureBonus01()
    {
        var cur = CurrentCaptureBand;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    public long GetLureSecondsRemaining()
    {
        var cur = CurrentLure;
        if (cur == null) return -1;
        long now = SaveManager.NowUnix();
        long rem = cur.expireUnix - now;
        return System.Math.Max(0L, rem);
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

    private static T PickByWeight<T>(IList<T> items, System.Func<T, float> getWeight)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = Mathf.Max(0f, getWeight(items[i]));
            if (!float.IsNaN(w) && !float.IsInfinity(w)) total += w;
        }

        if (total <= 0f) return items.Count > 0 ? items[Random.Range(0, items.Count)] : default;

        float roll = Random.Range(0f, total);
        float acc = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = Mathf.Max(0f, getWeight(items[i]));
            if (w <= 0f) continue;
            acc += w;
            if (roll <= acc) return items[i];
        }
        return items[items.Count - 1];
    }

    // ------------- BOSS HELPERS -------------

    private bool ShouldSpawnBoss(int encountersSinceBoss, int bossEveryN)
    {
        if (bossEveryN < 1) bossEveryN = 10;
        return encountersSinceBoss >= (bossEveryN - 1);
    }

    private MonsterDataSO PickBossWeighted(MonsterLibrarySO lib, string lastBossId)
    {
        if (!lib || lib.monsters == null || lib.monsters.Length == 0) return null;

        var pool = BuildBossPool(lib, lastBossId, allowUncatchableOnly: true);
        if (pool.Count == 0)
        {
            pool = BuildBossPool(lib, excludeId: null, allowUncatchableOnly: true);
            if (pool.Count == 0) return null;
        }

        int total = 0;
        for (int i = 0; i < pool.Count; i++)
            total += Mathf.Max(1, pool[i].bossWeight);

        int r = Random.Range(0, total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(1, pool[i].bossWeight);
            if (r < acc) return pool[i];
        }
        return pool[pool.Count - 1];
    }

    private List<MonsterDataSO> BuildBossPool(MonsterLibrarySO lib, string excludeId, bool allowUncatchableOnly)
    {
        var list = new List<MonsterDataSO>();
        foreach (var m in lib.monsters)
        {
            if (!m) continue;
            if (!m.isBoss) continue;
            if (allowUncatchableOnly && !m.uncatchable) continue;
            if (!string.IsNullOrEmpty(excludeId) && m.id == excludeId) continue;
            list.Add(m);
        }
        return list;
    }

    private void AfterBattleCadenceUpdate(ref int encountersSinceBoss, bool wasBoss, MonsterDataSO bossUsed, ref string lastBossId)
    {
        if (wasBoss)
        {
            encountersSinceBoss = 0;
            lastBossId = bossUsed ? bossUsed.id : null;
        }
        else
        {
            encountersSinceBoss = Mathf.Max(0, encountersSinceBoss + 1);
        }
    }

    // ===================== Idle helpers / State getters =========================

    // Maintained for compatibility (now backed by the regen system)
    public long GetLastSavedUnix() => SaveManager.Data.lastSavedUnix;

    public bool IsAutoModeAllowedInBackground()
    {
        if (inBattle) return false;
        return true;
    }

    // ---- State getters for UI ----
    public bool IsInBattle => inBattle;
    public bool IsAutoMode => autoMode;
    public bool NextEncounterIsFree => nextEncounterFree;

    // ---- helpers ----
    void EmitStatus(string msg, LogScope scope = LogScope.Encounter)
    {
        if (!string.IsNullOrEmpty(msg))
            BattleLogger.Log(msg, scope); // mirror to centralized log
        OnStatus?.Invoke(msg);           // legacy listeners
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

            // Only normalize when HP is uninitialized (=-1).
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

    // ── Win Streak (persist if save supports it) ────────────────────────────────
    private int LoadWinStreakOr(int fallback)
    {
        var data = SaveManager.Data;
        if (data == null) return fallback;

        try
        {
            var t = data.GetType();
            var f = t.GetField("currentWinStreak");
            if (f != null) return Mathf.Max(0, (int)f.GetValue(data));
            var p = t.GetProperty("currentWinStreak");
            if (p != null) return Mathf.Max(0, (int)p.GetValue(data, null));
        }
        catch { }
        return fallback;
    }

    private void SaveWinStreakIfPossible(int v)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        bool wrote = false;
        try
        {
            var t = data.GetType();
            var f = t.GetField("currentWinStreak");
            if (f != null) { f.SetValue(data, v); wrote = true; }
            var p = t.GetProperty("currentWinStreak");
            if (!wrote && p != null && p.CanWrite) { p.SetValue(data, v, null); wrote = true; }
        }
        catch { }

        if (wrote) SaveManager.Save();
    }

    private void SetWinStreak(int value)
    {
        int v = Mathf.Max(0, value);
        if (v == _currentWinStreak) return;

        _currentWinStreak = v;
        SaveWinStreakIfPossible(v);
        GameEvents.WinStreakChanged?.Invoke(_currentWinStreak);
        RequestStateRefresh();
    }

    private void ReconcileHPWithCurrentWinStreak()
    {
        var data = SaveManager.Data;
        var team = data?.team;
        var lib  = MonsterLibraryLocator.Lib;
        if (team == null || team.Count == 0 || !lib) return;

        for (int i = 0; i < team.Count; i++)
        {
            var owned = team[i];
            if (owned == null || string.IsNullOrEmpty(owned.monsterId)) continue;

            var def = lib.GetById(owned.monsterId);
            if (!def) continue;

            int baseMaxHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, Mathf.Max(1, owned.level)));
            float baseMaxF = Mathf.Max(1f, baseMaxHP);

            var ctx = TitleContext.Empty;
            ctx.winStreak = _currentWinStreak;

            float curHPf = Mathf.Max(0f, owned.currentHP);
            ctx.selfHp01 = Mathf.Clamp01(curHPf / baseMaxF);

            float finalMaxF = TitlesAdapter.GetStatValue(owned.monsterId, def, owned.level, "HP", ctx, baseMaxF);
            int finalMax = Mathf.Max(1, Mathf.RoundToInt(finalMaxF));

            if (owned.currentHP > finalMax)
            {
                owned.currentHP = finalMax;
                team[i] = owned;
            }
        }
    }

    // ====== Capture logic ======
    void TryCatch(MonsterDataSO def, int level)
    {
        if (!def) return;
        var data = SaveManager.Data;
        var lib  = MonsterLibraryLocator.Lib;
        if (data == null || !lib) return;

        if (def.uncatchable)
        {
            EmitStatus("(Capture skipped — uncatchable.)", LogScope.Encounter);
            return;
        }

        // Base chance from spawn weight → [15%, 65%]
        float minW = float.MaxValue, maxW = 0f;
        for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (!m) continue;
            float w = Mathf.Max(0f, m.spawnWeight);
            if (w < minW) minW = w;
            if (w > maxW) maxW = w;
        }
        if (minW == float.MaxValue || maxW <= 0f || minW >= maxW) { minW = 0f; maxW = 1f; }

        float t = Mathf.Clamp01((Mathf.Max(0f, def.spawnWeight) - minW) / Mathf.Max(0.0001f, (maxW - minW)));
        float baseChance = Mathf.Lerp(0.15f, 0.65f, t);

        float bandBonus = GetActiveCaptureBonus01() * 0.25f;
        float scarcity01 = 1f - t;
        float luckBonus  = GetActiveLuckBonus01() * 0.20f * Mathf.Clamp01(scarcity01 * 1.25f);
        float lureBonus  = 0f; var lure = CurrentLure; if (lure != null && lure.type == def.type) lureBonus = Mathf.Clamp01(lure.bonus) * 0.10f;
        float streakBonus = Mathf.Clamp01(CurrentWinStreak / 20f) * 0.05f;

        float finalChance = Mathf.Clamp01(baseChance + bandBonus + luckBonus + lureBonus + streakBonus);

        float roll = Random.value;
        bool success = (roll <= finalChance);

        if (success)
        {
            if (AudioManager.I)
                AudioManager.I.PlaySfx(SfxType.CaptureSuccess);

            var om = new OwnedMonsterData
            {
                monsterId = def.id,
                level = Mathf.Max(1, level),
                currentHP = -1,
                currentXP = 0,
                ownedUID = Guid.NewGuid().ToString("N")
            };
            data.owned ??= new List<OwnedMonsterData>();
            data.owned.Add(om);

            data.ownedIds ??= new HashSet<string>(); data.ownedIds.Add(def.id);
            data.seenTypes ??= new HashSet<MonsterType>(); data.seenTypes.Add(def.type);

            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();

            BattleLogger.Log($"🎉 Capture success! {def.displayName} (Lv {level}) joined your roster. [p={Mathf.RoundToInt(finalChance * 100f)}%]", LogScope.Encounter);
            EmitStatus($"Captured {def.displayName}! (Lv {level})", LogScope.Encounter);
        }
        else
        {
            BattleLogger.Log($"Capture failed on {def.displayName} (Lv {level}). [p={Mathf.RoundToInt(finalChance * 100f)}%, roll={Mathf.RoundToInt(roll * 100f)}%]", LogScope.Encounter);
            EmitStatus($"Capture failed. {def.displayName} escaped.", LogScope.Encounter);
        }
    }

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

        // Unique / special encounter
        if (IsUniqueMonster(wild))
        {
            AudioManager.I.PlaySfx(SfxType.UnqiueEncounter); // note: enum spelling
            return;
        }
    }

    private bool IsShinyMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            // Try field "isShiny"
            var f = t.GetField("isShiny", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                var val = f.GetValue(m);
                if (val is bool b) return b;
            }

            // Try property "isShiny"
            var p = t.GetProperty("isShiny", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanRead)
            {
                var val = p.GetValue(m, null);
                if (val is bool b) return b;
            }
        }
        catch { /* ignore and treat as non-shiny */ }

        return false;
    }

    private bool IsUniqueMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            // Try "isUniqueEncounter"
            var f1 = t.GetField("isUniqueEncounter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f1 != null)
            {
                var v = f1.GetValue(m);
                if (v is bool b1) return b1;
            }

            var p1 = t.GetProperty("isUniqueEncounter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p1 != null && p1.CanRead)
            {
                var v = p1.GetValue(m, null);
                if (v is bool b1p) return b1p;
            }

            // Fallback: "isUnique"
            var f2 = t.GetField("isUnique", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f2 != null)
            {
                var v = f2.GetValue(m);
                if (v is bool b2) return b2;
            }

            var p2 = t.GetProperty("isUnique", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p2 != null && p2.CanRead)
            {
                var v = p2.GetValue(m, null);
                if (v is bool b2p) return b2p;
            }
        }
        catch { /* ignore and treat as non-unique */ }

        return false;
    }


    // ── Blinders helpers (soft-optional) ────────────────────────────────────────
    void Blinders_Show()
    {
        try { _blindersShow?.Invoke(blindersBehaviour, null); } catch { }
    }
    void Blinders_Hide()
    {
        try { _blindersHide?.Invoke(blindersBehaviour, null); } catch { }
    }
}
