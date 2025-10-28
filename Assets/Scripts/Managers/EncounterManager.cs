using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;
using System;

public class EncounterManager : MonoBehaviour
{
    public static EncounterManager I { get; private set; }

    [Obsolete("UI no longer renders inline status. Use BattleLogger instead.")]
    public event Action<string> OnStatus;
    public event Action OnStateChanged;
    public event Action OnEnergyChanged;

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

    private bool _currentEncounterIsBoss = false;
    private MonsterDataSO _currentBossUsed = null;

    private bool inBattle;
    private bool autoMode;
    private bool nextEncounterFree;
    private bool autoRunPaidEnergy;

    private Coroutine postResultCo;
    private Coroutine autoLoopCo;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
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

        EnsureDailyEnergyReset();

        inBattle = false;
        autoMode = false;
        nextEncounterFree = false;
        autoRunPaidEnergy = false;

        ResourceBank.EnsureSize();

        EmitStatus("Tap ENCOUNTER to begin. Hold to toggle AUTO.", LogScope.System);
        OnStateChanged?.Invoke();

        NormalizeTeamHPIfUninitialized();
    }

    // ---------------- PUBLIC API (called from UI) ----------------

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
            EmitStatus("Out of energy! Come back tomorrow or earn more.", LogScope.System);
            return;
        }
        StartEncounter(spendEnergy: true);
    }

    public void ToggleAutoMode()
    {
        autoMode = !autoMode;

        // Idle Battles integration
        if (autoMode) IdleBattleManager.I?.EnableAuto();
        else IdleBattleManager.I?.DisableAuto();

        if (autoMode)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;

            if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }
            autoLoopCo = StartCoroutine(AutoLoop());

            if (!inBattle)
                EmitStatus("AUTO mode ON. Battling until defeat…", LogScope.System);
            else
                EmitStatus("AUTO mode ON. Will continue after this battle…", LogScope.System);
        }
        else
        {
            autoRunPaidEnergy = false;

            if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }

            EmitStatus("AUTO mode OFF. Tap ENCOUNTER for the next fight.", LogScope.System);
        }


        PostBattleSummaryManager.I?.SetAutoBattling(autoMode);
        OnStateChanged?.Invoke();
    }

    // ---------------- ENERGY ----------------

    public void AddEnergy(int amount, bool allowOvercap = true)
    {
        SaveManager.Data.encounterPoints += amount;
        if (!allowOvercap)
            SaveManager.Data.encounterPoints = Mathf.Min(
                SaveManager.Data.encounterPoints,
                SaveManager.Data.encounterMax);

        SaveManager.Save();
        OnEnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
    }

    public bool SpendEnergy()
    {
        if (!HasEnergy()) return false;
        SaveManager.Data.encounterPoints -= SaveManager.Data.encounterCost;
        if (SaveManager.Data.encounterPoints < 0) SaveManager.Data.encounterPoints = 0;
        SaveManager.Save();
        OnEnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

    public bool HasEnergy() => SaveManager.Data.encounterPoints >= SaveManager.Data.encounterCost;

    void EnsureDailyEnergyReset()
    {
        int today = SaveManager.TodayYMD();
        if (SaveManager.Data.lastEncounterResetYMD != today)
        {
            SaveManager.Data.lastEncounterResetYMD = today;
            if (SaveManager.Data.encounterPoints < SaveManager.Data.encounterMax)
                SaveManager.Data.encounterPoints = SaveManager.Data.encounterMax;
            SaveManager.Save();
            OnEnergyChanged?.Invoke();
        }

        OnStateChanged?.Invoke();
    }

    // ---------------- AUTO LOOP ----------------

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

        PostBattleSummaryManager.I?.NotifyEnergyDepleted();

        EmitStatus("AUTO stopped: no energy.", LogScope.System);
        OnStateChanged?.Invoke();
    }

    // ---------------- ENCOUNTER FLOW ----------------

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
                EmitStatus("Out of energy! Come back tomorrow or earn more.", LogScope.System);
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

        if (TapBoost.I) TapBoost.I.ResetEncounter();

        var p = SaveManager.Data.team[0];
        if (_currentEncounterIsBoss)
            EmitStatus($"⚠️ BOSS ENCOUNTER! {wild.displayName} (Lv {wildLevel}) appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");
        else
            EmitStatus($"Encounter! A wild {wild.displayName} (Lv {wildLevel}) appears.{(p.flatAtkBonus > 0 ? $" (+ATK {p.flatAtkBonus})" : "")}");

        // NEW: mark encounter lifecycle for the log
        BattleLogger.BeginEncounter(_currentEncounterIsBoss ? $"BOSS: {wild.displayName} Lv{wildLevel}" : $"{wild.displayName} Lv{wildLevel}");

        if (_currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossSpawned?.Invoke(_currentBossUsed.id, _currentBossUsed);

        inBattle = true;
        OnStateChanged?.Invoke();

        battleManager.Begin(wild, wildLevel, OnBattleEnded);
    }

    void OnBattleEnded(BattleResult result)
    {
        int coins = ApplyCoinsGainedMultiplier(result.coinsGained);

        EmitStatus(result.victory ? $"Victory! +{coins} coins" : "Defeat.");

        ResourceManager.I.Add(ResourceType.Coins, coins);

        OnStateChanged?.Invoke();

        if (result.victory && _currentEncounterIsBoss && _currentBossUsed != null)
            GameEvents.BossDefeated?.Invoke(_currentBossUsed.id);

        if (SaveManager.Data != null)
        {
            AfterBattleCadenceUpdate(
                ref SaveManager.Data.encountersSinceBoss,
                _currentEncounterIsBoss,
                _currentBossUsed,
                ref SaveManager.Data.lastBossId
            );
        }

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

        SaveManager.Save();

        // NEW: close encounter lifecycle in the log
        BattleLogger.EndEncounter(result.victory);

        if (postResultCo != null) { StopCoroutine(postResultCo); postResultCo = null; }
        postResultCo = StartCoroutine(PostResultFlow(result.victory));
    }

    string GetLastStatus() => null; // UI kept this before; retained for compatibility with AppendLine usage.
    string AppendLine(string a, string b) => string.IsNullOrEmpty(a) ? b : (a + "\n" + b);

    private int ApplyCoinsGainedMultiplier(int baseCoins)
    {
        if (baseCoins <= 0) return 0;
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return baseCoins;

        var ids = new List<string>(team.Count);
        for (int i = 0; i < team.Count; i++)
        {
            var om = team[i];
            if (om != null && !string.IsNullOrEmpty(om.monsterId))
                ids.Add(om.monsterId);
        }
        if (ids.Count == 0) return baseCoins;

        float mul = TagRuntime.GetCoinsGainedMultiplier(ids);
        int scaled = Mathf.Max(0, Mathf.FloorToInt(baseCoins * mul));
        return scaled;
    }

    IEnumerator PostResultFlow(bool victory)
    {
        yield return new WaitForSeconds(postResultDelay);
        inBattle = false;

        if (!victory)
        {
            nextEncounterFree = false;
            autoRunPaidEnergy = false;
            OnStateChanged?.Invoke();

            if (autoMode)
            {
                EmitStatus("Defeat. Retrying (AUTO)…", LogScope.System);
            }
            else
            {
                EmitStatus("Tap ENCOUNTER for the next fight.", LogScope.System);
            }
            yield break;
        }

        // WIN
        if (autoMode)
        {
            if (!autoRunPaidEnergy)
            {
                if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                autoRunPaidEnergy = true;
            }
            StartEncounter(false); // wins chain for free during AUTO
        }
        else
        {
            nextEncounterFree = true; // manual: next fight is free on win
            OnStateChanged?.Invoke();
            EmitStatus("Tap NEXT for another battle (free).", LogScope.System);
        }
    }

    // ==========================
    // ===== UPDATED TryCatch ===
    // ==========================
    void TryCatch(MonsterDataSO wild, int wildLevel)
    {
        if (wild != null && wild.uncatchable)
        {
            EmitStatus(AppendLine(GetLastStatus(), "(This Bitling can’t be captured.)"));
            return;
        }

        int taps = TapBoost.I ? TapBoost.I.TapsThisEncounter : 0;
        float chance = 0.25f + (taps * 0.02f) + (SaveManager.Data.tapLevel * 0.01f);

        if (SaveManager.Data?.activeCaptureBands != null)
        {
            foreach (var band in SaveManager.Data.activeCaptureBands)
            {
                if (band != null && band.expireUnix > SaveManager.NowUnix())
                    chance *= (1f + Mathf.Max(0f, band.bonus));
            }
        }

        OwnedMonsterData lead = (SaveManager.Data.team != null && SaveManager.Data.team.Count > 0)
            ? SaveManager.Data.team[0]
            : null;
        chance *= ShinySystems.LeadCaptureMult(lead);
        chance = Mathf.Clamp01(chance);

        bool caught = Random.value < chance;
        int pct = Mathf.RoundToInt(chance * 100f);

        if (!caught)
        {
            EmitStatus($"{wild.displayName} fled. ({pct}%)");
            return;
        }

        // ===== Successful capture path =====

        // Shiny roll
        float baseShinyOdds = 1f / 512f;
        float shinyBoost = GetActiveShinyBoostMult();
        float shinyP = Mathf.Clamp01(baseShinyOdds * shinyBoost);
        bool isShinyCatch = Random.value < shinyP;

        // Build the OwnedMonsterData with recalc sentinel for HP
        var caughtMon = new OwnedMonsterData
        {
            monsterId = wild.id,
            level     = Mathf.Max(1, wildLevel - 1),
            currentHP = -1, // let HP be recalculated by your normalization
            isShiny   = isShinyCatch,
            ownedUID  = Guid.NewGuid().ToString("N")
        };

        // Apply duplicate policy (respects settings toggle)
        string duplicateNote;
        bool added = DuplicateResolver.TryApplyOnCatch(caughtMon, wild, wildLevel, out duplicateNote);

        // If we truly added a new owned entry, auto-add to team if there is space
        if (added && SaveManager.Data.team.Count < 3)
        {
            SaveManager.Data.team.Add(new OwnedMonsterData
            {
                monsterId = caughtMon.monsterId,
                level     = caughtMon.level,
                currentHP = caughtMon.currentHP,
                isShiny   = caughtMon.isShiny,
                ownedUID  = caughtMon.ownedUID
            });

            if (isShinyCatch)
                EmitStatus($"✨ Caught SHINY {wild.displayName}! ({pct}%) → Joined your team.");
            else
                EmitStatus($"Caught {wild.displayName}! ({pct}%) → Joined your team.");
        }
        else
        {
            // Either duplicate converted to XP OR it was added but team is full
            if (added)
            {
                // Added to collection (team full)
                if (isShinyCatch)
                    EmitStatus($"✨ Caught SHINY {wild.displayName}! ({pct}%) → Sent to collection.");
                else
                    EmitStatus($"Caught {wild.displayName}! ({pct}%) → Sent to collection.");
            }
            else
            {
                // Converted to XP (single-keeper policy)
                if (!string.IsNullOrEmpty(duplicateNote))
                {
                    if (isShinyCatch)
                        EmitStatus($"✨ Caught SHINY {wild.displayName}! ({pct}%)\n{duplicateNote}");
                    else
                        EmitStatus($"Caught {wild.displayName}! ({pct}%)\n{duplicateNote}");
                }
                else
                {
                    EmitStatus(isShinyCatch
                        ? $"✨ Caught SHINY {wild.displayName}! ({pct}%)"
                        : $"Caught {wild.displayName}! ({pct}%)");
                }
            }
        }

        SaveManager.Save();
    }

    // ===== LURES / LUCK / SHINY (unchanged) =====

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

    // =======================
    // ==== Idle helpers =====
    // =======================

    public int GetEnergyPoints() => SaveManager.Data.encounterPoints;
    public int GetEncounterCost() => Mathf.Max(1, SaveManager.Data.encounterCost);
    public int GetEncounterMax() => SaveManager.Data.encounterMax;
    public long GetLastSavedUnix() => SaveManager.Data.lastSavedUnix;

    public bool SpendEnergyIfPossible()
    {
        if (SaveManager.Data.encounterPoints < SaveManager.Data.encounterCost) return false;
        SaveManager.Data.encounterPoints -= SaveManager.Data.encounterCost;
        if (SaveManager.Data.encounterPoints < 0) SaveManager.Data.encounterPoints = 0;
        SaveManager.Save();
        OnEnergyChanged?.Invoke();
        OnStateChanged?.Invoke();
        return true;
    }

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
            BattleLogger.Log(msg, scope); // NEW: mirror all status to the centralized log
        OnStatus?.Invoke(msg);           // Legacy event for any old listeners
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
            // Do NOT heal KO (0) or any real saved value (>=0).
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

}
